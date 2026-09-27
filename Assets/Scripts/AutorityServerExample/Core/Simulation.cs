using System.Collections.Generic;

namespace Rts.Lockstep
{
    /// <summary>
    /// The deterministic tick: (state N, commands of tick N) -> state N+1.
    /// Server and every client run exactly this code with exactly the same inputs,
    /// so they all end up with bit-identical worlds without ever sending unit positions.
    /// </summary>
    public static class Simulation
    {
        public const int TicksPerSecond = 20;
        public const double TickDuration = 1.0 / TicksPerSecond;
        public const int UnitsPerPlayer = 6;

        public static readonly Fix64 UnitSpeedPerTick = Fix64.FromRatio(1, 5); // 4 m/s at 20 Hz
        public static readonly Fix64 FormationSpacing = Fix64.FromRatio(3, 2);

        private static readonly FixVec2[] PlayerBases =
        {
            FixVec2.FromInt(-12, -6),
            FixVec2.FromInt(12, 6),
            FixVec2.FromInt(-12, 6),
            FixVec2.FromInt(12, -6),
        };

        public static void Step(World world, IReadOnlyList<Command> commands)
        {
            // 1. Orders, in the exact order the server sealed them into the frame.
            for (int i = 0; i < commands.Count; i++)
                Apply(world, commands[i]);

            // 2. Game logic, units in ascending Id order.
            foreach (Unit unit in world.Units)
                MoveUnit(unit);

            world.Tick++;
        }

        private static void Apply(World world, Command command)
        {
            switch (command.Type)
            {
                case CommandType.SpawnPlayer:
                    SpawnPlayer(world, command.PlayerId);
                    break;

                case CommandType.Move:
                {
                    // Validation lives inside the simulation: an invalid order is ignored
                    // the same way on every peer, so it can't cause a desync.
                    List<Unit> units = CollectOwnedUnits(world, command);
                    for (int i = 0; i < units.Count; i++)
                    {
                        units[i].Target = command.Target + FormationOffset(i, units.Count);
                        units[i].IsMoving = true;
                    }

                    break;
                }

                case CommandType.Stop:
                    foreach (Unit unit in CollectOwnedUnits(world, command))
                    {
                        unit.Target = unit.Position;
                        unit.IsMoving = false;
                    }

                    break;
            }
        }

        private static void SpawnPlayer(World world, byte playerId)
        {
            foreach (Unit unit in world.Units)
                if (unit.Owner == playerId)
                    return; // already spawned

            FixVec2 basePosition = PlayerBases[(playerId - 1) % PlayerBases.Length];
            for (int i = 0; i < UnitsPerPlayer; i++)
                world.SpawnUnit(playerId, basePosition + FormationOffset(i, UnitsPerPlayer));
        }

        private static void MoveUnit(Unit unit)
        {
            if (!unit.IsMoving)
                return;

            FixVec2 toTarget = unit.Target - unit.Position;
            Fix64 distance = toTarget.Magnitude;

            if (distance <= UnitSpeedPerTick)
            {
                unit.Position = unit.Target;
                unit.IsMoving = false;
                return;
            }

            unit.Position += toTarget * (UnitSpeedPerTick / distance);
        }

        private static List<Unit> CollectOwnedUnits(World world, Command command)
        {
            var result = new List<Unit>(command.UnitIds.Length);
            foreach (int id in command.UnitIds)
            {
                Unit unit = world.FindUnit(id);
                if (unit != null && unit.Owner == command.PlayerId && !result.Contains(unit))
                    result.Add(unit);
            }

            return result;
        }

        /// <summary>Square grid around the target so a group doesn't collapse into one point.</summary>
        private static FixVec2 FormationOffset(int index, int count)
        {
            int columns = 1;
            while (columns * columns < count)
                columns++;
            int rows = (count + columns - 1) / columns;

            int column = index % columns;
            int row = index / columns;

            // (2 * column - (columns - 1)) / 2 centers the grid on the target.
            return new FixVec2(
                FormationSpacing * (2 * column - (columns - 1)) / 2,
                FormationSpacing * (2 * row - (rows - 1)) / 2);
        }
    }
}
