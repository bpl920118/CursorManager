# 🖱️ CursorManager (鼠標管理器 - Windows 一鍵套用工具)

<p align="center">
  <img src="CursorApp/app.ico" alt="CursorManager Logo" width="96" height="96" />
</p>

<p align="center">
  <b>CursorManager 是一款專為 Windows 10 / 11 設計的現代化、安全且免安裝的滑鼠游標管理與一鍵即時切換工具。</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue.svg" alt="Platform" />
  <img src="https://img.shields.io/badge/.NET-8.0-purple.svg" alt=".NET 8.0" />
  <img src="https://img.shields.io/badge/License-MIT-green.svg" alt="License" />
  <img src="https://img.shields.io/badge/Version-v3.0.2-brightgreen.svg" alt="Version" />
</p>

---

## ✨ 核心特色 (Key Features)

- **⚡ 一鍵秒級套用**：底層即時切換系統游標，**免重開機、免進入 Windows 控制台**，點擊瞬間生效。
- **🎬 動態游標即時預覽 (Live .ANI Preview)**：內建 60 FPS RIFF/ANI 解碼器，軟體介面內直接播放動態游標的真實影格動作。
- **🎨 預覽底色自由切換**：支援深色、淺色、透明棋盤格 3 種預覽背景，黑色與透明游標也能看得一清二楚。
- **🎯 完美支援 15 項標準游標**：深度相容 Windows 11 桌面視窗管理器 (DWM)。
- **🛡️ 純淨安全無毒**：原生解析 `.ani`、`.cur`、`.png`、`.svg` 與 `install.inf`，**無需冒險執行任何來路不明的外部 `.exe` 安裝檔**。
- **📂 集中式游標庫管理**：
  - 支援拖曳資料夾或檔案快速匯入。
  - 支援主題即時關鍵字搜尋。
  - 支援右鍵快速重新命名、開啟資料夾、刪除主題。
  - 支援自訂資料夾存放路徑與一站式設定面板。
- **📸 系統游標一鍵提取**：自動辨識並完整提取當前系統正在運行的 15 項游標，建立精確對映並永久保存入庫。
- **🔄 一秒原廠還原**：隨時一鍵還原回 Windows 官方原生預設樣式。

---

## 🖥️ 介面預覽 (Preview)

| 功能區塊 | 說明 |
| :--- | :--- |
| **左側游標庫清單** | 瀏覽與搜尋所有已收錄的游標主題，右鍵支援重命名/刪除/開檔 |
| **右側 15 格預覽** | 顯示 15 項標準游標（正常、文字、忙碌、縮放等），動態游標即時播放 |
| **頂部快速拖曳區** | 支援拖曳任意游標資料夾自動配對或匯入 |
| **頂部設定面板** | 統一管理資料夾儲存位置與 15 格預覽背景切換 |
| **底部狀態與操作列** | 即時提示套用狀態，提供一鍵套用、還原預設與線上版本檢查更新 |

---

## 🚀 快速開始 (Quick Start)

### 📥 下載獨立免安裝執行檔 (Recommended)
您無需安裝任何開發環境或 .NET 運行庫，直接下載打包好的單一執行檔即可使用：
👉 **[前往 GitHub Releases 下載最新版「CursorManager.exe」](https://github.com/bpl920118/CursorManager/releases/latest)**

1. 下載 `CursorManager.exe`。
2. 放置於任意資料夾（建議單獨建一個資料夾，以便自動收錄主題）。
3. 雙擊直接開啟即可使用！

---

### 🖱️ 套用主題步驟
1. 在左側主題清單中點擊想要套用的主題。
2. 點擊右下角 **「✨ 一鍵套用此主題」**。
3. 游標立即生效切換！

### 📂 匯入新游標
- 下載網路上的游標壓縮包後，解壓縮得到包含 `.ani` 或 `.cur` 的資料夾。
- **直接將該資料夾拖進軟體視窗**，點選「是 (Yes)」即可永久收錄至游標庫！

---

## 🛠️ 開發與編譯 (Build from Source)

### 系統環境要求
- Windows 10 / 11 (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 或更高版本
- Visual Studio 2022 / VS Code (具備 C# Dev Kit)

### 編譯發布指令
```bash
# 複製專案（clone 後資料夾名稱為 CursorManager）
git clone https://github.com/bpl920118/CursorManager.git
cd CursorManager/CursorApp

# 發布單一執行檔（發布選項已寫入 CursorApp.csproj）
dotnet publish -c Release -o ../PublishOut
```
編譯完成後，`CursorManager.exe` 會出現在 `PublishOut/` 目錄。

或在專案根目錄雙擊執行 `重新編譯更新.bat`（會直接將 exe 輸出到根目錄）。

---

## 📁 專案架構 (Project Architecture)

```
CursorManager/                  # Git 倉庫根目錄（git clone 後的資料夾名稱）
├── CursorApp/                  # WPF 應用程式核心原始碼
│   ├── App.xaml / App.xaml.cs  # 單一實例控制 (Mutex) 與全域異常攔截
│   ├── MainWindow.xaml (.cs)   # 主介面與核心 UI/事件控制邏輯
│   ├── CursorInstaller.cs      # Windows 註冊表持久化與 Win32 API 底層置換核心
│   ├── CursorMatcher.cs        # 智慧游標比對引擎 (支援 INF/關鍵字/正則/Fallback)
│   ├── CursorIconHelper.cs     # RIFF ANI 動畫解析器、圖標快取與 GDI 資源管理
│   ├── FolderScanner.cs        # 多執行緒並行資料夾掃描器 (Parallel)
│   ├── ImportScanner.cs        # 安全匯入掃描（.zip 解壓、格式辨識）
│   ├── UpdateChecker.cs        # GitHub Release 版本檢查
│   ├── UpdateDownloader.cs     # 程式內下載更新檔
│   ├── UpdateInstaller.cs      # 一鍵安裝更新並重啟
│   ├── Models.cs               # 資料模型 (INotifyPropertyChanged)
│   ├── RenameDialog.xaml (.cs) # 主題重新命名彈窗
│   ├── SettingsDialog.xaml (.cs)# 游標儲存庫路徑設定彈窗
│   ├── ConfirmDialog.xaml (.cs)# 匯入確認對話框
│   └── UpdateDialog.xaml (.cs) # 檢查更新對話框
├── .gitignore                  # Git 忽略配置
├── LICENSE                     # MIT 開源授權
├── README.md                   # 專案詳細說明文檔
├── CONTRIBUTING.md             # 貢獻指南
├── 重新編譯更新.bat            # 本機快速發布單一 exe
└── 使用說明.txt                # 繁體中文快速使用手冊
```

---

## 📄 開源授權 (License)

本專案採用 [MIT License](LICENSE) 授權釋出，可自由修改、分發及商業/非商業使用。

---

## 🤝 貢獻指南 (Contributing)

歡迎提交 Issue 與 Pull Request！若有任何建議或新功能構想，請參閱 [CONTRIBUTING.md](CONTRIBUTING.md)。
