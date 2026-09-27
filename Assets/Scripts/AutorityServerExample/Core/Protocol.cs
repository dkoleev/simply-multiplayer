using System;
using System.IO;

namespace Rts.Lockstep
{
    public enum MessageType : byte
    {
        Join = 1,      // client -> server: "let me in"
        Snapshot = 2,  // server -> client: your player id + full world (late join / resync)
        Command = 3,   // client -> server: a player order
        TickFrame = 4, // server -> clients: all orders to execute at tick N
        StateHash = 5, // client -> server: "my world hash after tick N is X"
    }

    /// <summary>
    /// The heartbeat of lockstep. The server seals one frame per tick, even an empty one:
    /// a client may simulate tick N only after it has received frame N.
    /// </summary>
    public sealed class TickFrame
    {
        public int Tick;
        public Command[] Commands;

        public TickFrame(int tick, Command[] commands)
        {
            Tick = tick;
            Commands = commands;
        }

        public void Write(BinaryWriter w)
        {
            w.Write(Tick);
            w.Write((ushort)Commands.Length);
            foreach (Command command in Commands)
                command.Write(w);
        }

        public static TickFrame Read(BinaryReader r)
        {
            int tick = r.ReadInt32();
            var commands = new Command[r.ReadUInt16()];
            for (int i = 0; i < commands.Length; i++)
                commands[i] = Command.Read(r);
            return new TickFrame(tick, commands);
        }
    }

    /// <summary>Message layout: [MessageType:byte][payload]. Little-endian, via BinaryWriter.</summary>
    public static class Protocol
    {
        public static byte[] Join() => Build(MessageType.Join, w => { });

        public static byte[] Snapshot(byte playerId, World world) => Build(MessageType.Snapshot, w =>
        {
            w.Write(playerId);
            world.Write(w);
        });

        public static byte[] Command(Command command) => Build(MessageType.Command, command.Write);

        public static byte[] TickFrame(TickFrame frame) => Build(MessageType.TickFrame, frame.Write);

        public static byte[] StateHash(int tick, uint hash) => Build(MessageType.StateHash, w =>
        {
            w.Write(tick);
            w.Write(hash);
        });

        /// <summary>Returns a reader positioned right after the message type.</summary>
        public static BinaryReader Open(byte[] data, out MessageType type)
        {
            var reader = new BinaryReader(new MemoryStream(data));
            type = (MessageType)reader.ReadByte();
            return reader;
        }

        private static byte[] Build(MessageType type, Action<BinaryWriter> writeBody)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)type);
                writeBody(writer);
                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
