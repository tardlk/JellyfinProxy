<p align="center">
  <img src="Properties/thumb.png" width="120" alt="JellyfinProxy" />
</p>

<h1 align="center">JellyfinProxy</h1>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&style=flat-square" />
  <img src="https://img.shields.io/badge/Jellyfin-10.10+-AA5CC3?logo=jellyfin&style=flat-square" />
  <img src="https://img.shields.io/badge/license-MIT-blue?style=flat-square" />
</p>

<p align="center"><b>Jellyfin 网络优化插件</b><br>内置本地代理，支持选择代理 / TMDB 地址替代 / 强制 IPv4<br>解决刮削元数据时 TMDB / TVDB / FanArt 的网络连通问题</p>

---

## ✨ 功能

所有功能通过内置本地代理实现，无需额外安装代理软件：

| 功能 | 说明 |
|------|------|
| 🌐 选择代理 | 可配外部代理，所有请求走代理 |
| 🔄 TMDB 替代 | `api.themoviedb.org` / `image.tmdb.org` 重定向到自建镜像 |
| 📡 强制 IPv4 | 指定域名仅用 IPv4，避免 IPv6 超时 |

---

## ⚡ 安装

### 方法一：插件仓库安装（推荐）

1. Jellyfin 控制台 → 插件 → 仓库 → 添加
2. 填入：`https://raw.githubusercontent.com/tardlk/JellyfinProxy/master/manifest.json`
3. 在插件目录找到 **JellyfinProxy**，点击安装
4. 重启 Jellyfin

### 方法二：手动安装

1. 从 [Releases](../../releases) 下载 `JellyfinProxy.zip`
2. 解压到 `data/plugins/JellyfinProxy/`
3. 重启 Jellyfin

---

## 🔧 配置

安装后在 **控制台 → 插件 → JellyfinProxy** 配置：

| 配置项 | 说明 |
|--------|------|
| 外部代理地址 | `http://user:pass@host:port`，留空直连 |
| TMDB API 地址 | 替换 `api.themoviedb.org` 如 `https://api.tmdb.org` |
| TMDB 图片地址 | 替换 `image.tmdb.org`，留空不替换 |
| IPv4 域名列表 | 一行一个，如 `image.tmdb.org` |
| 调试模式 | 输出详细请求日志 |

> ⚠️ 修改配置后需重启 Jellyfin 生效。

---

## 📦 本地编译

```bash
git clone https://github.com/tardlk/JellyfinProxy.git
cd JellyfinProxy
dotnet build -c Release
```

需要 .NET 8 SDK，产物：`bin/Release/net8.0/JellyfinProxy.dll`

---

## 📄 License

MIT © [tardlk](https://github.com/tardlk)
