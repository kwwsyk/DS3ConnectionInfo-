# Steam 资料与游戏时长字段

“悬浮层显示字段”新增四个独立字段：账号公开性、艾尔登法环、艾尔登法环：黑夜君临、黑神话：悟空。沿用现有窗口/悬浮层同步开关、拖动排序和悬浮层文字颜色设置。旧设置的列身份和顺序保留，新增字段默认隐藏。全部隐藏时不会启动新查询。

窗口使用 😎 表示非公开资料（包含私密和仅好友），悬浮层用“非公开”文字，避免几何描边文字的 emoji 字体问题。未知用 ? 表示，不将请求失败当成私密。

两个列表均使用嵌入资源图标。只有该游戏累计时长大于 0 才显示图标；0 小时仍显示 0h，未知显示 ?。当该游戏时长严格大于同一玩家的黑魂3时长时，方形图标外围显示 2 像素黄色框。任一时长未知时不加框。没有返回游戏记录不能判定未拥有。XML 时长可能已经取整，前缀 ~ 表示近似；此路径的比较按返回时长进行。

设置可选环境变量 STEAM_WEB_API_KEY 后重启程序，使用官方 Steam Web API。未设置、空字符串或空白则使用社区 XML。游戏 XML 有时返回登录页，此时保持未知。不会读取浏览器 Cookie，不会尝试绕过隐私设置。配置了无效 Key 时保留 API 失败状态，不切换 XML。

每个 Player 对象成功查询后间隔 10 分钟刷新，失败后间隔 1 分钟重试；最多两个玩家同时查询。断开重连的新对象会重新查询。更新由现有 UI 定时器重新绑定，因此不会阻塞 ETW 延迟监测。

## 资源来源

三个 JPG 来自 Steam 官方商店资源，仅用作相应游戏的识别缩略图；方形控件裁剪显示，并非自制游戏标志。

- 艾尔登法环：https://cdn.akamai.steamstatic.com/steam/apps/1245620/capsule_231x87.jpg
- 黑夜君临：Steam 商店 appdetails（2622380）的 header_image
- 黑神话悟空：https://cdn.akamai.steamstatic.com/steam/apps/2358720/capsule_231x87.jpg

## 验证

Windows 下运行 BUILDING.md 的回归命令。SteamInfoTests 使用模拟 HTTP 响应，检查 Key 路径、XML 路径、非公开资料、登录 HTML、错误账号、缺失/零时长、相等时长和 DS3 未知情况，不请求真实 Steam 数据。另需在 Windows 游戏内检查图标清晰度、黄色边框及开关保存。

## 四项均显示未知时的排查

窗口中悬停账号公开性或游戏字段，可查看实际查询源（XML / Web API）、目标 Steam ID、查询状态和 HTTP 状态码。网络失败、超时、HTTP错误、需登录、响应异常分别显示，不再统一吞成问号。? 只表示响应中没有足够可见数据，不能说明账号私密。

资料公开与游戏详情公开分别控制。即使资料 XML 成功，游戏 XML 也可能跳转登录页，此时显示“需登录”；没有可靠的匿名游戏列表时，不伪造时长。免 Key 模式的可用性不能保证；配置个人 Key 可切换官方 API，但目标游戏详情仍须可见。

## 浏览器经代理能访问，但程序显示网络失败

程序默认使用 .NET Framework 的 Windows Internet 代理设置，这可能与浏览器扩展、另一个用户的配置或本地代理软件不同。可设置独立的 HTTP 代理覆盖默认设置（适用于 XML 和 Web API）：

```powershell
# 7890 仅为示例，必须替换成代理软件实际的 HTTP / mixed 端口。
[Environment]::SetEnvironmentVariable("DS3_STEAM_PROXY", "http://127.0.0.1:7890", "User")
```

完全退出程序及启动它的 Visual Studio/终端后重新打开。代理软件必须处于运行状态，配置地址为 HTTP 代理地址；即使目标为 HTTPS，仍使用 http://127.0.0.1:端口 来建立 CONNECT 隧道。当前 .NET Framework 路径不支持 socks5:// 配置，不接收带凭据或路径的代理 URL，不关闭证书验证，也不自动扫描本机代理端口。

恢复 Windows 默认代理设置：

```powershell
[Environment]::SetEnvironmentVariable("DS3_STEAM_PROXY", $null, "User")
```

悬停字段可查看网络路径和底层错误类型/代码。NameResolutionFailure 表示域名解析失败，ProxyNameResolutionFailure 表示代理域名解析失败，ConnectionRefused 表示连接被拒绝（使用本地代理时请先检查端口和进程），TrustFailure / AuthenticationException 指向 TLS/证书问题。诊断不会输出原始异常消息、API Key 或代理凭据。连接成功后，游戏 XML 仍可能显示“需登录”，这是独立的匿名数据可见性限制。
