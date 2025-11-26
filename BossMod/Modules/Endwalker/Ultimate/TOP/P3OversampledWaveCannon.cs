using System;
using System.Collections.Generic;
using System.Linq;
using BossMod;
using BossMod.Components;

namespace BossMod.Endwalker.Ultimate.TOP
{
    // P3 Oversampled Wave Cannon の散会処理
    public sealed class P3OversampledWaveCannon : BossModule
    {
        private readonly Dictionary<int, WPos> _safeSpots = new();
        private MonitorAssignment _assignment;

        private static readonly int MT = 0, OT = 1, H1 = 2, H2 = 3, M1 = 4, M2 = 5, R1 = 6, R2 = 7;

        public P3OversampledWaveCannon(WorldState ws, Actor primary)
            : base(ws, primary, new(100, 100), new ArenaBoundsCircle(30)) { }

        public override void OnStatusGain(Actor actor, ref ActorStatus status) => UpdateAssignment();
        public override void OnStatusLose(Actor actor, ref ActorStatus status) => UpdateAssignment();

        private void UpdateAssignment()
        {
            // Monitor 状態を取得
            bool[] monitorFlags = new bool[8];
            for (int i = 0; i < 8; i++)
                monitorFlags[i] = Raid[i]?.HasStatus(SID.Monitor) ?? false;

            _assignment = new MonitorAssignment(monitorFlags);
            CalculateSafeSpots();
        }

        private void CalculateSafeSpots()
        {
            _safeSpots.Clear();
            var center = Arena.Center;

            // 散会座標オフセット
            var offsets = new WDir[4] {
                new WDir(0, +11),  // 北
                new WDir(+11, 0),  // 東
                new WDir(0, -11),  // 南
                new WDir(-11, 0)   // 西
            };

            for (int slot = 0; slot < 8; slot++)
            {
                int pos = _assignment.position[slot];
                _safeSpots[slot] = center + offsets[pos];
            }
        }

        public override void DrawArenaForeground(int pcSlot, Actor pc)
        {
            foreach (var kv in _safeSpots)
            {
                var color = kv.Key == pcSlot ? Colors.Me : Colors.Safe;
                Arena.AddCircle(kv.Value, 1.0f, color);
            }
        }

        // Monitor/Normal 散会決定ルール
        private class MonitorAssignment
        {
            public readonly int[] position = new int[8];

            public MonitorAssignment(bool[] monitor)
            {
                // 初期散会: N=H1, MT / E=OT, H2 / S=M2, R2 / W=M1, R1
                position[MT] = 0; position[H1] = 0;
                position[OT] = 1; position[H2] = 1;
                position[M2] = 2; position[R2] = 2;
                position[M1] = 3; position[R1] = 3;

                Adjust(monitor);
            }

            private void Adjust(bool[] monitor)
            {
                // THグループ(MT,OT,H1,H2) / DPSグループ(M1,M2,R1,R2)
                int[] TH = { MT, OT, H1, H2 };
                int[] DPS = { M1, M2, R1, R2 };

                AdjustGroup(TH, monitor);
                AdjustGroup(DPS, monitor);
            }

            private void AdjustGroup(int[] group, bool[] monitor)
            {
                int monitorCount = group.Count(i => monitor[i]);

                switch (monitorCount)
                {
                    case 0: return; // 調整なし
                    case 1:
                        // 横軸に1人になるようにペア交代
                        int solo = group.First(i => monitor[i]);
                        int pair = group.First(i => !monitor[i]);
                        Swap(solo, pair);
                        break;
                    case 2:
                        // 横1縦1になるよう調整
                        int[] monitors = group.Where(i => monitor[i]).ToArray();
                        if (position[monitors[0]] == position[monitors[1]])
                        {
                            // 同軸に並んでいる場合、近接同士を交代
                            Swap(monitors[0], group.First(i => !monitor[i]));
                        }
                        break;
                    case 3:
                        // 縦軸2人の場合は検知0人のペアと交代
                        int[] nonMonitors = group.Where(i => !monitor[i]).ToArray();
                        if (nonMonitors.Length == 1)
                        {
                            Swap(nonMonitors[0], group.First(i => monitor[i] && position[i] != position[nonMonitors[0]]));
                        }
                        break;
                }
            }

            private void Swap(int a, int b)
            {
                int tmp = position[a];
                position[a] = position[b];
                position[b] = tmp;
            }
        }
    }
}
