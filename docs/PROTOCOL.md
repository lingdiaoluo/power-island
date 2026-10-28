# 协议说明

记录电量岛目前实现的读取方式，方便补新型号。所有实现都在
`PowerIsland/Services/VendorProtocols.cs` 与 `HidInventory.cs`。

## 1. 设备识别

用 SetupAPI 枚举 HID 类设备（`GUID 4D1E55B2-F16F-11CF-88CB-001111000030`），逐个打开并读：

- `HidD_GetAttributes` → VID / PID
- `HidD_GetProductString` / `HidD_GetManufacturerString` → 名称
- `HidD_GetPreparsedData` + `HidP_GetCaps` → `UsagePage` / `Usage` / 各报告长度
- `HidD_GetFeature` → 试探性读取 Feature 报告（诊断用）

一个接收器会暴露多个顶层集合（鼠标数据、多媒体键、系统键、厂商通道…）。
**电量只走厂商通道**，用 `UsagePage + Usage` 定位，例如 VGN F1 的 `0xFF02 / 0x0002`。

以 VGN F1 MOBA 的接收器（`VID_3554 & PID_F503`，厂商 Compx）为例：

| 集合 | UsagePage / Usage | 输入 | 输出 | Feature |
| --- | --- | --- | --- | --- |
| col01 | FF05 / 0000 | 8 | – | – |
| col02 | FF03 / 0000 | 8 | – | – |
| col03 | 000C / 0001 | 3 | – | – |
| col04 | 0001 / 0080 | 2 | – | – |
| **col05** | **FF02 / 0002** | **17** | **17** | – |
| col06 | FF04 / 0002 | – | – | 8 |

## 2. Compx 17 字节协议（协议 1）

适用于 VGN F1 MOBA / F1 Pro、VXE R1 Pro Max / R1 SE+、VXE MAD R / Major+、ATK F1 Ultimate / A9 Ultimate。

握有读写权限（`GENERIC_READ | GENERIC_WRITE`）打开 **FF02/0002** 集合后：

**请求**（17 字节，作为 Output 报告写入）：

```
08 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 49
│  │                                      │
│  └ 0x04 命令字                          └ 0x49 固定尾字节
└ Report ID 0x08
```

**应答**（17 字节 Input 报告）：

```
08 04 00 00 00 02 5F 00 10 05 …
│  │        │  │
│  │        │  └ [7] 连接方式：0 = 2.4G 无线，非 0 = 有线
│  │        └ [6] 电量百分比（0–100）
│  └ [1] 回显命令字 0x04
└ [0] Report ID 0x08
```

校验建议：`res[0] == 0x08 && res[1] == 0x04`，且 `res[6] <= 100`。
实测应答一般在 3 ms 内返回；鼠标休眠时**完全不返回**。

## 3. Compx 64 字节协议（协议 2）

适用于 ATK Zero 一类 Nordic 54L15 方案，集合为 **FF05/0001**：

**请求**（64 字节）：

```
08 7D 72 02 00 01 07 01 00 …
│  │  │           │
│  │  └ 0x72 命令 │
│  └ 0x7D 无线（有线为 0x7C，且 [5]=0x00）
└ Report ID 0x08
```

**应答**校验：`res[1] == 0x72 && res[5] == 0x07`，无线模式下 `res[7]` 为电量。

> 有线模式该协议不返回可靠电量，程序会保留上一次的数值。

## 4. 蓝牙设备

不碰私有协议，直接读 Windows 自己缓存的关联端点（AEP）属性：

```csharp
var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
var props = new[] { "System.Devices.BatteryLife", "System.Devices.Aep.IsConnected" };
var devices = await DeviceInformation.FindAllAsync(selector, props,
    DeviceInformationKind.AssociationEndpoint);
```

`System.Devices.BatteryLife` 是 `byte`，与「设置 → 蓝牙」显示的电量同源。
没有蓝牙适配器时该调用可能长时间不返回，所以程序把它放在 HID 结果之后，并加 1.5 秒超时。

## 5. Win32 上的坑（重要）

1. **同步 `ReadFile` 会永久阻塞**
   设备不回包时，同步句柄的 `ReadFile` 不会超时。因此每台设备使用一个**常驻后台读线程**：
   写入请求后就地阻塞等应答，界面线程只等一个有界事件（1.5 秒），超时即放弃本次。

2. **挂起的读会堵住后续命令**
   接收器一次只处理一个挂起请求，旧的读不结束，新命令就排在后面，看起来像设备「死」了。
   做法是：等待超时后立刻 `CloseHandle` 取消这次挂起的读，把接收器让给下一轮查询。

3. **句柄必须读写都打开**
   只给 `GENERIC_READ` 会导致 `WriteFile` 失败（`ERROR_ACCESS_DENIED`）。

4. **重叠 I/O 在这个通道上不工作**
   用 `FILE_FLAG_OVERLAPPED` 打开时 `WriteFile` 能返回成功，但设备不产生应答，
   `ReadFile` 一直 pending。所以最终选择了「同步句柄 + 常驻读线程 + 取消挂起」的方案。

5. **不要用 `WaitForSingleObject(handle)` 判断有无数据**
   同步句柄在写入完成后会一直处于已信号状态，等到信号不代表有报告可读。

## 6. 新增型号

1. 插上接收器，用 `HidInventory` 或任何 HID 查看器（如 WebHID 的 USB 设备查看器）确认
   `VID / PID` 与厂商集合的 `UsagePage / Usage`。
2. 用 `HidD_GetFeature` 或直接读写 17 / 64 字节报告，找出电量所在字节。
3. 在 `KnownDevices.All` 里加一条：

```csharp
new("型号名", "副标题信息", 0x3554, 0xF503, 0xF502, 0xFF02, 0x0002, 1)
//  名称        元信息        VID    无线PID  有线PID  UsagePage Usage 协议号
```

4. 如果协议字节布局不同，在 `CompxReader.Reader` 里加一个新的 `BuildProtocolN` / `Parse` 分支。

欢迎带 **HID 报告描述符 + 请求/应答十六进制** 提 issue。
