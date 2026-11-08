using System;
using NebulaAPI;
using NebulaAPI.Networking;

namespace EnergyHub
{
    /// <summary>
    /// 联机适配（1.19.5）：直接依赖 Nebula 的**官方公开 API**（NebulaAPI.dll），
    /// 和创世之书用的是同一套做法。
    ///
    /// 为什么这条路走得通，而之前那个 <c>DSPEnergyHub.Nebula.bin</c> 不行：
    ///   · NebulaAPI 是官方给第三方 mod 用的稳定接口：<c>BasePacketProcessor&lt;T&gt;</c> +
    ///     <c>[RegisterPacketProcessor]</c> + <c>RegisterPackets(assembly)</c>，全都 public；
    ///   · 发布包里**不带** NebulaAPI.dll（Thunderstore 有独立依赖包
    ///     <c>NebulaModTeam-NebulaMultiplayerModApi</c>，安装时自动拉），
    ///     所以插件目录仍然只有 <c>DSPEnergyHub.dll</c> 一个文件；
    ///   · 那个 API 包自己写着 “Does NOT require Nebula”，纯单机玩家装上没有任何副作用。
    ///
    /// 分工：主机（房主）是唯一权威，负责能量结算、扣料、升级判定；
    /// 客户端只回放主机广播的状态，并在本地操作后把那一台上报给主机。
    /// </summary>
    public static class MP
    {
        /// <summary>主机每 0.5 秒广播一次全量状态。</summary>
        public const float BroadcastInterval = 0.5f;

        private static bool _registered;

        /// <summary>启动时打一行状态，便于看日志确认联机层是否就位。</summary>
        public static void Init()
        {
            if (_registered)
                return;
            _registered = true;

            // 扫描本程序集里所有 [RegisterPacketProcessor] 的处理器。
            NebulaModAPI.RegisterPackets(typeof(MP).Assembly);

            // 客户端进/回星球时先向主机要一份该星球的枢纽快照：
            // 光靠 0.5 秒轮询广播，落地那一下会先显示 0 再跳变。
            NebulaModAPI.OnPlanetLoadRequest += planetId =>
            {
                if (!IsClient)
                    return;
                try
                {
                    NebulaModAPI.MultiplayerSession.Network.SendPacket(new HubRequestPacket(planetId));
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("[联机] 请求星球 " + planetId + " 的枢纽状态失败: " + e.Message);
                }
            };

            // 包到的时候工厂还没加载完，先存着，等 OnPlanetLoadFinished 再落地。
            NebulaModAPI.OnPlanetLoadFinished += planetId =>
            {
                if (!IsClient)
                    return;
                HubManager.FlushPendingPlanet(planetId);
            };

            Plugin.Log.LogInfo("联机层就绪（NebulaAPI 官方接口）：主机结算 + 客户端回放，状态 " + StatusText);
        }

        /// <summary>本模组编译期就引用了 NebulaAPI，所以恒为 true。</summary>
        public static bool HasNebula { get { return true; } }

        /// <summary>当前是否处于联机会话中（单机时为 false）。</summary>
        public static bool Active
        {
            get
            {
                try { return NebulaModAPI.IsMultiplayerActive; }
                catch { return false; }
            }
        }

        /// <summary>本端是否为客户端（联机且不是房主）。</summary>
        public static bool IsClient
        {
            get
            {
                if (!Active)
                    return false;
                try { return !NebulaModAPI.MultiplayerSession.LocalPlayer.IsHost; }
                catch { return false; }
            }
        }

        /// <summary>本端是否为权威端：单机恒 true，联机时只有房主是 true。</summary>
        public static bool IsHost
        {
            get
            {
                if (!Active)
                    return true;
                try { return NebulaModAPI.MultiplayerSession.LocalPlayer.IsHost; }
                catch { return true; }
            }
        }

        /// <summary>能量结算是否在本端执行。客户端为 false（只回放主机状态）。</summary>
        public static bool Authoritative { get { return IsHost; } }

        /// <summary>是否需要把本地操作上报给主机（只有联机客户端需要）。</summary>
        public static bool CanReport { get { return IsClient; } }

        /// <summary>面板 / 日志用的一行状态。</summary>
        public static string StatusText
        {
            get
            {
                if (!Active) return "单机";
                return IsHost ? "联机 · 主机" : "联机 · 客户端";
            }
        }

        /// <summary>主机向所有客户端广播；客户端向主机发送。</summary>
        public static bool Send(HubStatePacket packet)
        {
            if (packet == null || !Active)
                return false;
            try
            {
                NebulaModAPI.MultiplayerSession.Network.SendPacket(packet);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[联机] 发送枢纽状态失败: " + e.Message);
                return false;
            }
        }

        /// <summary>处理器收到包之后转给 HubManager（统一在一处做应用逻辑）。</summary>
        internal static void Dispatch(byte[] data, bool fromHost, bool snapshot)
        {
            if (data == null || data.Length == 0)
                return;
            try { HubManager.OnNetData(data, fromHost, snapshot); }
            catch (Exception e) { Plugin.Log.LogWarning("[联机] 应用枢纽状态失败: " + e.Message); }
        }
    }
}
