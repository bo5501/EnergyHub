"""查 Thunderstore 上本包的实际上架状态。

用法: python tools/ts_status.py [namespace] [package]
默认 BOBO550 / EnergyHubTower（包名只能用 [A-Za-z0-9_]；团队名是 BOBO550，**不是** GitHub 的 bo5501）。
"""
import json
import sys
import urllib.request

NS = sys.argv[1] if len(sys.argv) > 1 else "BOBO550"
PKG = sys.argv[2] if len(sys.argv) > 2 else "EnergyHubTower"


def main():
    url = "https://thunderstore.io/api/experimental/package/%s/%s/" % (NS, PKG)
    try:
        with urllib.request.urlopen(
            urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"}), timeout=30
        ) as r:
            d = json.load(r)
    except Exception as e:
        print("查询失败: %s" % e)
        print("包/团队不存在（或命名空间写错）: %s" % url)
        return

    lat = d.get("latest") or {}
    print("包        : %s" % d.get("full_name"))
    print("创建/更新 : %s / %s" % (d.get("date_created"), d.get("date_updated")))
    print("最新版本  : %s（%s）" % (lat.get("version_number"), lat.get("date_created")))
    print("已废弃    : %s" % d.get("is_deprecated"))
    print("描述      : %s" % lat.get("description"))

    print("\n各社区上架状态:")
    for l in d.get("community_listings") or []:
        st = l.get("review_status")
        flag = {
            "approved": "✅ 正常在架",
            "rejected": "❌ 已被版主拒绝：社区页 404，mod manager 里也搜不到",
            "unreviewed": "… 待审核（需要审核的社区此时不可见）",
        }.get(st, st)
        print("  [%s] %s" % (l.get("community"), flag))
        print("    NSFW 标记 : %s%s" % (
            l.get("has_nsfw_content"),
            "   <-- 上传时误勾了「包含成人内容」，DSP 不是 NSFW 社区，会被判 Invalid submission。"
            "该标记不能事后改、也不跨版本继承：发新版本上传时选 No 即可清掉" if l.get("has_nsfw_content") else ""))
        print("    分类      : %s" % ", ".join(l.get("categories") or []))
        if st == "rejected":
            print("    -> 处理：登录后打开包页面可看到拒绝理由；到 Thunderstore Discord 的")
            print("       #rejected-uploads 发帖（或 contact@thunderstore.io）请求重新审核。")


if __name__ == "__main__":
    main()
