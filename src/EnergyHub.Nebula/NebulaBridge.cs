using System;
using System.IO;
using EnergyHub;
using NebulaAPI;
using NebulaAPI.GameState;
using NebulaAPI.Networking;
using NebulaAPI.Packets;

namespace EnergyHub.Nebula
{
    /// <summary>
    /// 联机子模块（1.14.0）。只在玩家装了 Nebula 时才会被主模块反射加载。
    ///
    /// 职责很窄：
    ///   · Init —— 把本程序集里的 [RegisterPacketProcessor] 注册给 Nebula；
    ///   · IsActive / IsClient —— 供主模块判断本端该不该结算；
    ///   · Broadcast —— 主机每 0.5 秒把全部枢纽状态发给客户端。
    ///
    /// 为什么用「状态广播」而不是「操作同步」：
    /// 我们的结算依赖本地电网、卡槽、跨星球 pending 账本，让客户端各算一遍必然发散。
    /// 主机算完把结果（模式 / 等级 / 池储量 / 对接 / 累计）发下去，客户端只做显示，
    /// 逻辑上天然一致，也不会触发 Nebula 的 desync 检测。
    /// </summary>
    public static class NebulaBridge
    {
        private const float Interval = 0.5f;

        private static float _timer;
        private static bool _registered;

        public static void Init()
        {
            if (_registered)
                return;

            NebulaModAPI.RegisterPackets(typeof(NebulaBridge).Assembly);
            _registered = true;

            Plugin.Log.LogInfo("联机子模块已注册：HubStatePacket（主机 → 客户端，0.5 秒/次）");
        }

        public static bool IsActive()
        {
            return NebulaModAPI.IsMultiplayerActive;
        }

        public static bool IsClient()
        {
            IMultiplayerSession session = NebulaModAPI.MultiplayerSession;
            return session != null && session.IsClient;
        }

        public static void Broadcast(float dt)
        {
            _timer += dt;
            if (_timer < Interval)
                return;
            _timer = 0f;

            IMultiplayerSession session = NebulaModAPI.MultiplayerSession;
            if (session == null || session.Network == null)
                return;

            byte[] data;
            using (MemoryStream ms = new MemoryStream(4096))
            {
                using (BinaryWriter w = new BinaryWriter(ms))
                    HubManager.CollectStates(w);
                data = ms.ToArray();
            }

            // 只有 count（4 字节）说明一台枢纽都没有，不必发空包
            if (data.Length <= 4)
                return;

            session.Network.SendPacket(new HubStatePacket { Data = data });
        }
    }

    /// <summary>
    /// 主机 → 客户端的枢纽状态包。
    /// 负载统一塞进 byte[] 自己序列化：字段少、版本演进时只改两端读写顺序，
    /// 不用维护一堆并行的 int[]/long[] 数组，也避开 LiteNetLib 对复杂类型的限制。
    /// </summary>
    public class HubStatePacket
    {
        public byte[] Data;
    }

    [RegisterPacketProcessor]
    public class HubStateProcessor : BasePacketProcessor<HubStatePacket>
    {
        public override void ProcessPacket(HubStatePacket packet, INebulaConnection conn)
        {
            if (packet == null || packet.Data == null || packet.Data.Length < 4)
                return;

            try
            {
                using (MemoryStream ms = new MemoryStream(packet.Data))
                using (BinaryReader r = new BinaryReader(ms))
                    HubManager.ApplyStates(r);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("应用主机状态失败: " + e.Message);
            }
        }
    }
}
