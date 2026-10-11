# 网络框架

核心位于 Alloy.Runtime，不依赖 Protobuf 或项目业务类型。
NetworkManager 管理会话和服务注册表；NetworkServiceRegistry 接收项目直接创建的服务实例，
提供按类型获取和逆序释放。项目在自己的启动管线中安装网络。

NetworkSession 负责请求关联、超时、取消和消息派发，INetworkTransport 抽象字节收发。
TcpNetworkTransport 提供 TCP 实现，工作线程只处理 socket 与有界队列，业务回调在主线程 Tick 执行。
不自动重连、重发或安装心跳，连接成功不代表认证成功。

Runtime/NetProtobuf 与 Editor/Network 在 ALLOY_PROTOBUF 条件下启用。
生成器根据项目协议输出官方消息、Service 收发分部、回调骨架、解析器与服务注册表。
手写 Service 回调由业务实现，生成器保留既有逻辑；框架不生成 Model 或 Controller。

编辑器菜单位于 `★AlloyFramework★/网络`，包含协议生成和网络调试。
框架与模板不包含测试协议、业务 Service 或本地服务器管理工具。
TCP 不支持 WebGL，Android 后台策略和认证由项目实现。
