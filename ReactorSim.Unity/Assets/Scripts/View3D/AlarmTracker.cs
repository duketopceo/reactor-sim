using System;
using System.Collections.Generic;

// Pure logic (no UnityEngine): annunciator acknowledge state (U17, R18). Linked into ReactorSim.Core.Tests.
//   a raised alarm is Unacked (its tile FLASHES) until the operator acknowledges,
//   then Acked (the tile holds STEADY) until the condition clears,
//   then Clear; a later raise starts again at Unacked.
// Acknowledge is the Core intent AcknowledgeAlarms: the plant counts them (PlantSnapshot.AlarmAckSeq) and every change of
// that count acknowledges the alarms that were already flashing. The first update only learns the count, so a count
// restored from a save does not retro-acknowledge anything raised since.
namespace ReactorSim.Instruments
{
    public enum AlarmState
    {
        Clear,
        Unacked,
        Acked
    }

    public sealed class AlarmTracker
    {
        private readonly Dictionary<string, AlarmState> states =
            new Dictionary<string, AlarmState>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> scratch = new List<string>();
        private int lastSeq;
        private bool seeded;

        public AlarmState StateOf(string alarm)
        {
            AlarmState st;
            return alarm != null && states.TryGetValue(alarm, out st) ? st : AlarmState.Clear;
        }

        /// <summary>True when any alarm is raised and not yet acknowledged.</summary>
        public bool AnyUnacked
        {
            get
            {
                foreach (var kv in states) if (kv.Value == AlarmState.Unacked) return true;
                return false;
            }
        }

        /// <summary>Alarms that became Unacked in the latest Update (a new alarm to announce).</summary>
        public int NewlyRaised { get; private set; }

        /// <summary>
        /// Advances the tracker. isRaised answers for each alarm name in `alarms` (indexed, no enumerator); ackSeq is the
        /// plant's acknowledge count.
        /// </summary>
        public void Update(IList<string> alarms, Func<string, bool> isRaised, int ackSeq)
        {
            bool acked = seeded && ackSeq != lastSeq;
            lastSeq = ackSeq;
            seeded = true;
            NewlyRaised = 0;

            if (acked)
            {
                scratch.Clear();
                foreach (var kv in states) if (kv.Value == AlarmState.Unacked) scratch.Add(kv.Key);
                for (int i = 0; i < scratch.Count; i++) states[scratch[i]] = AlarmState.Acked;
            }

            for (int a = 0; a < alarms.Count; a++)
            {
                string name = alarms[a];
                AlarmState st = StateOf(name);
                if (isRaised(name))
                {
                    if (st == AlarmState.Clear)
                    {
                        states[name] = AlarmState.Unacked;
                        NewlyRaised++;
                    }
                }
                else if (st != AlarmState.Clear)
                {
                    states[name] = AlarmState.Clear;
                }
            }
        }
    }
}
