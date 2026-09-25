# 提交给 BoBppy/g3mpro-macos 的 Issue

发布地址：https://github.com/BoBppy/g3mpro-macos/issues/new

---

## 标题

```
Key-mapping write (0x09) has no effect on 320F:706E — am I missing a step?
```

## 正文

```markdown
Hi — thanks for publishing the protocol work, it has been extremely useful. I have been
building a Windows equivalent (WinUI 3 settings app + a small WinForms tray host) from
your `CHIDBridge.c`, and I reimplemented everything against a real device.

Reads all work, and every other write path verifies byte-for-byte on hardware:

- `0x03` header, `0x05`/`0x06` profile read/write, `0x07` keymap read, `0x1A`/`0x06`
  battery, `0x28` stage, `0x29` polling rate, `0xAA` ping
- As a control I wrote back an **unmodified** 84-byte profile via `0x06` and the readback
  matched byte-for-byte — so no firmware-side rewriting is hiding a verify failure.

**But I cannot get the key-mapping write to take effect.**

Device: `VID 320F / PID 706E`, usage page `0xFF1C` usage `0x0092`, 64-byte reports in
both directions, no report-ID prefix, 42 key slots, sensor 13205, chunk size 24.
Header: `AA 55 00 00 20 2A 18 00 01 95 33 32 07 02 32 00 …`

Each attempt below was followed by a fresh `0x07` readback of the full table:

| Path | Result |
| --- | --- |
| `0x0B` (your live-preview path), offset 0, no session | **Rejected** — `response[7] == 0xFF`. This matches your comment about this firmware rejecting it at offset 0. |
| `0x09` inside a `0x01`/`0x02` session, offset 0, chunk 24 | Accepted, `response[7] == 0x00`, session ends cleanly — **readback unchanged** |
| `0x08`, same framing | Accepted, same non-effect |
| `0x09` followed by an `0x06` profile write in the *same* session (guessing at a block commit) | No effect |

**A control experiment that may be relevant:** a *known-good* write — `0x06` with
byte-identical content — **also echoes the previous content** in `response[8..]`. So the
echo returned by `0x08`/`0x09` does not distinguish a read from a write. By response shape
they look like real writers; they simply do not change the table.

### Questions

1. **Was the `0x09` persistent write actually confirmed on real hardware?** Your README
   lists 「退出映射恢复」 among the items still to be checked on a device before release,
   so I was not sure whether that path had been exercised end-to-end. If it was verified,
   I would love to know what I am doing differently.

2. **Is there a step the original Windows driver performs that is not in `CHIDBridge.c`?**
   For example a command before/after the write, a different offset for writing than for
   reading, a length or end-of-table marker, or a commit step separate from `0x02`.

3. **Which receiver firmware did you test against?** If your header differs from the one
   above, that would explain everything immediately.

4. **Does `0x09` share the same offset space as `0x07`?** I only tried offset 0.

Happy to run any hypothesis you want on the Windows side — I have a working test harness
and can iterate quickly. If it turns out the path never worked on this firmware, that is
useful to know too, and I will document it rather than chase it.
```

---

## 中文版（如需改用中文，替换正文即可）

```markdown
你好，感谢你把协议逆向结果公开出来，对我的帮助非常大。我基于 `CHIDBridge.c` 做了一个
Windows 端的等价实现（WinUI 3 设置程序 + 一个很小的 WinForms 托盘宿主），并且已经在真机上
把协议重新实现并逐条验证过了。

读取全部正常，其他每一条写入路径都在真机上逐字节校验通过：

- `0x03` 设备头、`0x05`/`0x06` 配置读写、`0x07` 按键表读取、`0x1A`/`0x06` 电量、
  `0x28` 档位、`0x29` 回报率、`0xAA` 在线探测
- 作为对照，我用 `0x06` 把**未改动**的 84 字节配置原样写回，读回结果逐字节一致，
  所以不存在「固件自行改写某些字节导致校验失败」的干扰。

**但我无法让按键映射的写入生效。**

设备：`VID 320F / PID 706E`，用途页 `0xFF1C` 用途 `0x0092`，收发各 64 字节、无 Report ID 前缀，
按键槽位 42 个，传感器 13205，分块 24 字节。
设备头：`AA 55 00 00 20 2A 18 00 01 95 33 32 07 02 32 00 …`

下面每一次尝试之后，我都会重新用 `0x07` 完整读回映射表：

| 尝试的通道 | 结果 |
| --- | --- |
| `0x0B`（你的易失实时通道），偏移 0，不包会话 | **被拒绝** —— `response[7] == 0xFF`。和你注释里说的「该固件在偏移 0 拒绝」一致 |
| `0x09` 包在 `0x01`/`0x02` 会话里，偏移 0，分块 24 | 被接受，`response[7] == 0x00`，会话正常结束 —— **读回内容毫无变化** |
| `0x08`，同样的组织方式 | 被接受，同样无效 |
| `0x09` 之后在**同一个会话内**再发一次 `0x06` 配置写入（猜测需要整体提交） | 无效 |

**一个可能相关的对照实验：** 已知可用的 `0x06` 写入，即使内容完全相同，
**响应里同样会回显旧数据**。所以 `0x08`/`0x09` 返回的回显并不能区分读和写。
从响应格式看它们确实像真正的写命令，只是对映射表不起作用。

### 想请教的问题

1. **`0x09` 的持久写入路径，你那边是在真机上实际确认过的吗？** 你的 README 把
   「退出映射恢复」列为真机发布前仍需检查的项目，所以我不确定这条路径是否被端到端跑通过。
   如果确认可用，我很想知道我哪里做得不一样。

2. **原 Windows 驱动里是否有 `CHIDBridge.c` 里没有体现的步骤？** 比如写入前后还有别的命令、
   写入偏移和读取偏移不同、需要长度或表结束标记、或者除 `0x02` 之外还有一个提交步骤。

3. **你当时测试的接收器固件是哪个版本？** 如果设备头和上面这串不一样，那就一下子解释清楚了。

4. **`0x09` 和 `0x07` 是同一套偏移空间吗？** 我只试了偏移 0。

Windows 这边我可以随时验证你提出的任何猜想，测试脚手架是现成的，迭代很快。
如果结论是这条路径在这个固件上从来就没生效过，那也很有价值，我会直接写进文档，不再深挖。
```

---

## 说明

- 我**没有**替你发布。本机没有可用的 GitHub 凭据，而翻找凭据这件事本身也不该做。
- 想让我以后能直接发 issue，装一下 `gh` 并 `gh auth login` 即可（`winget install GitHub.cli`）。
- 发之前建议核对一处：正文里说「其他每条写入路径都逐字节校验通过」——这台设备上确认可用的是
  DPI 档位、当前档位、回报率、直线修正、波纹修正、按键防抖、静默高度，属实。
