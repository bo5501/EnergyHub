using System;
using NebulaAPI;
using NebulaAPI.Networking;
using NebulaAPI.Packets;

namespace EnergyHub
{
    /// <summary>
    /// 枢纽状态包。
    ///
    /// <see cref="Kind"/> 区分方向，避免两个包类型：
    ///   0 = Snapshot：主机 → 所有客户端，一次带多台（每 0.5 秒；进星球时也用它）；
    ///   1 = Report  ：客户端 → 主机，只带刚刚操作过的那一台。
    ///
    /// 负载用自己拼的 <see cref="System.IO.BinaryWriter"/>（见 HubManager.WriteHub），
    /// 不依赖 Nebula 的序列化规则，字段增删只影响我们自己。
    /// </summary>
    public class HubStatePacket
    {
        public HubStatePacket() { }

        public HubStatePacket(byte kind, byte[] data)
        {
            Kind = kind;
            Data = data;
        }

        public byte Kind { get; set; }
        public byte[] Data { get; set; }
    }

    [RegisterPacketProcessor]
    public class HubStateProcessor : BasePacketProcessor<HubStatePacket>
    {
        public override void ProcessPacket(HubStatePacket packet, INebulaConnection conn)
        {
            if (packet == null || packet.Data == null || packet.Data.Length == 0)
                return;

            bool iAmHost = MP.IsHost;
            bool snapshot = packet.Kind == 0;

            // 主机收到客户端上报：先落到自己的记录上（这样发送端立刻知道有人连过来了），
            // 再转发给其它客户端 —— 不然同一局里的第三个人看不到这次操作。
            if (iAmHost && !snapshot)
            {
                MP.Dispatch(packet.Data, false, false);
                try { NebulaModAPI.MultiplayerSession.Network.SendPacket(packet); }
                catch { /* 有人正好掉线：本地已经应用了，转发失败不影响主流程 */ }
                return;
            }

            // 主机收到快照（自己的包被回环 / 另一台主机）：不需要，忽略。
            if (iAmHost && snapshot)
                return;

            // 客户端收到主机的东西：回放。
            MP.Dispatch(packet.Data, true, snapshot);
        }
    }

    /// <summary>客户端进/回星球时，向主机要一次该星球的枢纽快照。</summary>
    public class HubRequestPacket
    {
        public HubRequestPacket() { }

        public HubRequestPacket(int planetId) { PlanetId = planetId; }

        public int PlanetId { get; set; }
    }

    [RegisterPacketProcessor]
    public class HubRequestProcessor : BasePacketProcessor<HubRequestPacket>
    {
        public override void ProcessPacket(HubRequestPacket packet, INebulaConnection conn)
        {
            if (packet == null || conn == null)
                return;
            if (!MP.IsHost)
                return;

            byte[] data;
            try { data = HubManager.BuildSnapshot(packet.PlanetId); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[联机] 导出星球 " + packet.PlanetId + " 的枢纽状态失败: " + e.Message);
                return;
            }

            if (data == null || data.Length == 0)
                return;

            try { conn.SendPacket(new HubStatePacket(0, data)); }
            catch (Exception e) { Plugin.Log.LogWarning("[联机] 回发枢纽快照失败: " + e.Message); }
        }
    }
}
