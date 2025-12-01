namespace BossMod.Endwalker.Ultimate.TOP;

sealed class P3OversampledWaveCannonSafe : BossComponent
{
    private Actor? _boss;
    private Angle _bossAngle;
    private readonly Angle[] _playerAngles = new Angle[PartyState.MaxPartySize];
    private readonly int[] _playerOrder = new int[PartyState.MaxPartySize];
    private int _numPlayerAngles;
    private readonly List<int> _monitorOrder = new();

    private readonly TOPConfig _config = Service.Config.Get<TOPConfig>();

    private static readonly AOEShapeRect _shape = new(50, 50);

    private static readonly Dictionary<string, WPos> BasePositions = new()
    {
        ["Center"] = new WPos(100, 100),
        ["NorthNear"] = new WPos(100, 90.5f),
        ["NorthFar"]  = new WPos(100, 81.0f),
        ["SouthNear"] = new WPos(100, 109.5f),
        ["SouthFar"]  = new WPos(100, 119.0f),
        ["EastNear"]  = new WPos(109.5f, 100),
        ["EastFar"]   = new WPos(119.0f, 100),
        ["WestNear"]  = new WPos(90.5f, 100),
        ["WestFar"]   = new WPos(81.0f, 100),
    };

    private bool IsTH(int slot) => Raid[slot].Role is Role.Tank or Role.Healer;
    private bool IsDPS(int slot) => Raid[slot].Role is Role.Melee or Role.Ranged;
    private bool IsMonitor(int slot) => _playerAngles[slot] != default;

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (_playerOrder[slot] != 0)
            hints.Add($"Order: {(IsMonitor(slot) ? "M" : "N")}{_playerOrder[slot]}", false);

        int numHitBy = AOEs(slot).Count(a => !a.source && _shape.Check(actor.Position, a.origin, a.rot));
        if (numHitBy != 1)
            hints.Add($"Hit by {numHitBy} monitors!");
    }

    public override void AddMovementHints(int slot, Actor actor, MovementHints movementHints)
    {
        foreach (var p in SafeSpots(slot).Where(p => p.assigned))
            movementHints.Add(actor.Position, p.pos, Colors.Safe);
    }

    public override void DrawArenaBackground(int pcSlot, Actor pc)
    {
        foreach (var a in AOEs(pcSlot))
            _shape.Draw(Arena, a.origin, a.rot, a.safe ? Colors.SafeFromAOE : default);
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        foreach (var p in SafeSpots(pcSlot))
            Arena.AddCircle(p.pos, 1f, p.assigned ? Colors.Safe : default);
    }

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        var angle = status.ID switch
        {
            (uint)SID.OversampledWaveCannonLoadingL => 90f.Degrees(),
            (uint)SID.OversampledWaveCannonLoadingR => -90f.Degrees(),
            _ => default
        };

        if (angle != default && Raid.FindSlot(actor.InstanceID) is var slot && slot >= 0)
        {
            _playerAngles[slot] = angle;
            if (++_numPlayerAngles == 3)
                AssignPlayerOrder();
        }
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        var angle = spell.Action.ID switch
        {
            (uint)AID.OversampledWaveCannonL => 90f.Degrees(),
            (uint)AID.OversampledWaveCannonR => -90f.Degrees(),
            _ => default
        };
        if (angle != default)
        {
            _boss = caster;
            _bossAngle = angle;
        }
    }

    private void AssignPlayerOrder()
    {
        int n = 0, m = 0;
        foreach (var sg in _config.P3MonitorsAssignments.Resolve(Raid).OrderBy(sg => sg.group))
        {
            _playerOrder[sg.slot] = IsMonitor(sg.slot) ? ++m : ++n;
            if (IsMonitor(sg.slot))
                _monitorOrder.Add(sg.slot);
        }
    }

    private List<(WPos pos, bool assigned)> SafeSpots(int slot)
    {
        var safespots = new List<(WPos, bool)>();
        if (_numPlayerAngles < 3 || _bossAngle == default)
            return safespots;

        var thGroup = Raid.WithSlot().Where(t => IsTH(t.slot)).ToList();
        var dpsGroup = Raid.WithSlot().Where(t => IsDPS(t.slot)).ToList();

        safespots.AddRange(AssignGroupSafeSpots(thGroup, slot));
        safespots.AddRange(AssignGroupSafeSpots(dpsGroup, slot));

        return safespots;
    }

    private List<(WPos pos, bool assigned)> AssignGroupSafeSpots(List<(int slot, Actor actor)> group, int currentSlot)
    {
        var spots = new List<(WPos, bool)>();
        int monitorCount = group.Count(p => IsMonitor(p.slot));

        for (int i = 0; i < group.Count; i++)
        {
            var (slot, actor) = group[i];
            WPos pos = BasePositions["Center"];

            if (monitorCount == 0) pos = BasePositions["Center"];
            else if (monitorCount == 1) pos = (i == 0) ? BasePositions["WestNear"] : BasePositions["EastNear"];
            else if (monitorCount == 2) pos = (i == 0) ? BasePositions["WestNear"] : BasePositions["NorthNear"];
            else if (monitorCount == 3) pos = i switch { 0 => BasePositions["WestNear"], 1 => BasePositions["EastNear"], _ => BasePositions["NorthFar"] };

            spots.Add((pos, slot == currentSlot));
        }

        return spots;
    }

    private (WPos origin, Angle rot, bool safe, bool source)[] AOEs(int slot)
    {
        var isMonitor = IsMonitor(slot);
        var order = (isMonitor, _playerOrder[slot]) switch
        {
            (_, 1) => 2,
            (true, _) => 0,
            (_, 2 or 3) => 1,
            _ => 3,
        };
        var aoes = AOEs();
        var len = aoes.Length;
        var aoesNew = new (WPos, Angle, bool, bool)[len];
        var index = 0;
        for (var i = 0; i < len; ++i)
        {
            ref readonly var aoe = ref aoes[i];
            if (aoe.origin != null)
            {
                aoesNew[index++] = (aoe.origin.Position, aoe.origin.Rotation + aoe.offset, aoe.order == order, isMonitor && aoe.order == _playerOrder[slot]);
            }
        }
        return aoesNew[..index];
    }

    private (Actor? origin, Angle offset, int order)[] AOEs()
    {
        var count = _monitorOrder.Count;
        var aoes = new (Actor?, Angle, int)[count + 1];
        aoes[0] = (_boss, _bossAngle, 0);
        for (var i = 0; i < _monitorOrder.Count; ++i)
        {
            var s = _monitorOrder[i];
            aoes[i + 1] = (Raid[s], _playerAngles[s], i + 1);
        }
        return aoes;
    }
}
