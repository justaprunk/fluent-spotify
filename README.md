<p align="center">
  <img src="Assets/AppIcon.png" alt="Fluent Spotify" width="96" height="96" />
</p>

<h1 align="center">Fluent Spotify</h1>

<p align="center">
  An unofficial Fluent Design Spotify client for Windows, built with WinUI 3
</p>

<p align="center">
  <img alt="Windows 10/11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white" />
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" />
  <img alt="WinUI 3" src="https://img.shields.io/badge/WinUI%203-Windows%20App%20SDK%201.6-005FB8" />
  <img alt="Spotify Web API" src="https://img.shields.io/badge/Spotify-Web%20API-1DB954?logo=spotify&logoColor=white" />
</p>

<p align="center">
  <b>English</b> · <a href="README.ru.md">Русский</a>
</p>

<!-- Screenshot: put the file at docs/screenshot.png and uncomment the line below
<p align="center"><img src="docs/screenshot.png" alt="Fluent Spotify screenshot" width="860" /></p>
-->

---

## ✨ What it is

**Fluent Spotify** is a native Windows app with a Windows 11 style interface: Mica, Acrylic, NavigationView and the system theme.
It manages your library and playback through the **Spotify Web API**.

The app does not play audio itself. The **official Spotify client** plays the music as a Spotify Connect device, and Fluent Spotify acts as its interface.
By default, the app launches the official client **hidden in the background** and transfers playback to it when nothing is playing.
If music is already playing on another device, such as your phone or another PC, the app leaves that device alone.

## 🎧 Features

| Feature | Description |
|---|---|
| 🔐 **PKCE sign-in** | Sign in through the browser with no client secret. Tokens are stored encrypted |
| 🏠 **Home** | Your playlists and Liked Songs |
| 📃 **Playlists and Liked Songs** | Tracks load page by page, so the UI never blocks |
| 📊 **My summary** | Top tracks, artists and genres over about a year, 6 months or 4 weeks, like Spotify Wrapped. You can save the summary as a private playlist |
| 🔎 **Search** | Tracks, artists and albums |
| ▶️ **Player bar** | Seek, volume, shuffle and repeat |
| ⌨️ **Windows media keys** | System Media Transport Controls (SMTC) support |
| 🎨 **Appearance** | Window backdrop (Mica, Mica Alt, Acrylic, Acrylic Thin or none) and theme (system, light, dark) |
| 🐚 **WSHM shell theme** | When enabled, the backdrop and theme come from `~/.config/wshm/theme.json` and follow the shell |
| 🌐 **15 interface languages** | English, French, German, Dutch, Japanese, Korean, Polish, Portuguese (Brazil), Russian, Simplified and Traditional Chinese, Spanish (Latin America and Spain), Turkish and Ukrainian. The app follows the Windows display language by default; you can choose another one in Settings |
| 📐 **Responsive layout** | Works in narrow windows too. Minimum size is 520×560 |

> [!NOTE]
> When "Launch Spotify in the background" is on, the official client shows the Windows media controls itself. The app's own SMTC bridge is turned off in this mode so Windows doesn't show two media cards.

## ⬇️ Download

Get the latest installer from **[Releases](https://github.com/justaprunk/fluent-spotify/releases/latest)**: `FluentSpotify-<version>-Setup-x64.exe`.

- Installs for the current user into `%LOCALAPPDATA%\Programs\Fluent Spotify`, so it doesn't need admin rights.
- Includes the .NET runtime and Windows App SDK, so you don't need to install anything else.
- Adds a Start menu shortcut (and a desktop one if you want). Uninstall from **Settings → Apps**.

> [!NOTE]
> The installer isn't code-signed yet, so Windows SmartScreen may warn you. Click **More info → Run anyway**. You can check the file against the `.sha256` checksum attached to the release.
## 📋 Requirements

- **Windows 10 1809** (build 17763) or later, or **Windows 11**, x64
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** to build
- **The official Spotify client**, from the [Microsoft Store](https://apps.microsoft.com/detail/9ncbcszsjrsb) or [spotify.com](https://www.spotify.com/download/windows/)
- **Spotify Premium**: without it, the Web API does not allow playback control

You don't need to install the Windows App Runtime separately. The project builds with `WindowsAppSDKSelfContained`, so the Windows App SDK runtime ships next to the exe.

## 🔑 Spotify API setup

The app needs its own **Client ID**. It uses PKCE, so it doesn't need a client secret.

1. Open the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard) and click **Create app**.
2. Under **Redirect URIs**, enter exactly:

   ```text
   http://127.0.0.1:5000/callback/
   ```

   > [!IMPORTANT]
   > Spotify does not accept `localhost` in redirect URIs, so it has to be `127.0.0.1`. The trailing `/` matters too.

3. Under **Which API/SDKs are you planning to use?**, check **Web API**.
4. New apps run in **Development Mode**. Add your Spotify account (name and e-mail) under **User Management**, or sign-in will fail.
5. Copy the **Client ID** (32 hex characters) and give it to the app in one of these ways:
   - enter it on the sign-in screen and click "Сохранить Client ID" (Save Client ID);
   - set the `FLUENT_SPOTIFY_CLIENT_ID` environment variable.

   If both are set, the environment variable wins.

<details>
<summary><b>⚠️ Development Mode limits</b></summary>

<br />

Since February 2026, Spotify restricts the Web API for apps in Development Mode:

| What | Limit |
|---|---|
| Playlist contents | Only available for playlists you own or collaborate on. For other playlists Spotify returns **403**, and the app shows a clear message |
| Search | At most **10** results per request |
| Users | Only accounts listed under **User Management** can sign in |
| Artist genres | The field is deprecated and often comes back empty, so the top genres in My summary may be unavailable |

The client also works with apps that have Extended Quota access, since it supports the older API field names as well.

</details>

## 🛠️ Build and run

```powershell
# 1. Clone the repository
git clone https://github.com/justaprunk/fluent-spotify.git
cd fluent-spotify

# 2. Build (Release, x64)
dotnet build -c Release -p:Platform=x64

# 3. Publish to a folder for distribution (optional)
dotnet publish -c Release -r win-x64 --self-contained false -p:Platform=x64
```

After building, `fluent-spotify.exe` is in `bin\x64\Release\net8.0-windows10.0.22621.0\`.

To build the installer yourself (needs [Inno Setup 6](https://jrsoftware.org/isinfo.php): `winget install JRSoftware.InnoSetup`):

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

The script publishes a self-contained build and writes `installer\Output\FluentSpotify-<version>-Setup-x64.exe` plus its `.sha256` and `.sha256.sig`. Without the maintainer's signing key, add `-Unsigned`: the installer works, but the in-app updater won't accept it.

### Releases and update signing

The in-app updater installs a release only if it is signed with the maintainer's offline key, so a compromised GitHub account can't push an update to users.

- **Key.** ECDSA P-256, created once with `installer\signing-key.ps1 -New`. The private key lives outside the repo in `%USERPROFILE%\.fluent-spotify\update-signing-key.dpapi`, encrypted with Windows DPAPI for the current user and readable only by that account. The public key (SubjectPublicKeyInfo, base64) is embedded in `Core/Updates/UpdateSignature.cs`. Back the key up offline with `pwsh -File installer\signing-key.ps1 -Export <file>` (password-protected PKCS#8) and restore it with `-Import <file>`. The script refuses to write copies into a git repository or a cloud-synced folder.
- **Recovery key.** A second key, created with `pwsh -File installer\signing-key.ps1 -NewRecovery <USB path>`, exists only as a password-protected file on an offline USB drive; its public key is embedded next to the main one, and the app accepts a signature from either. If the main key is lost or leaked, sign a release with `-SignWith <recovery.pem> -File <installer>.sha256` and replace the key list in that release. **If both keys are lost, installed copies can't be updated anymore and users have to reinstall manually.**
- **Signature.** `build-installer.ps1` signs the exact bytes of `<installer>.sha256` (the hash plus the file name with its version) with ECDSA/SHA-256. `<installer>.sha256.sig` holds one line: the base64 IEEE P1363 signature (`r || s`, 64 bytes).
- **Check in the app.** `UpdateService` only offers releases that have all three assets on `github.com/justaprunk/fluent-spotify/releases/download/`. Before launching the installer it verifies the signature of `.sha256`, checks that the signed file name and version match the release (this blocks re-uploading an old signed build as a new version), then checks the SHA-256 of the downloaded installer through a write-locked handle.
- **Publishing a release.** Bump the version in `fluent-spotify.csproj` and `installer\FluentSpotify.iss`, run `build-installer.ps1`, and attach all three files from `installer\Output\` to the GitHub release: `.exe`, `.exe.sha256` and `.exe.sha256.sig`.
- **Transition.** Releases up to 1.3.2 have no signature. Their updaters don't check signatures and install 1.3.3 as usual. From 1.3.3 on, the app ignores unsigned releases.
- **Testing (Debug builds only).** `FLUENT_SPOTIFY_UPDATE_FEED=<url>` replaces the GitHub releases API with your own list in the same format. Assets may then come from the same host, but they still have to be signed with the embedded key.

> [!TIP]
> With `--self-contained false`, the target machine needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

## 🗂️ Project structure

<details>
<summary>Expand</summary>

```text
fluent-spotify/
├── App.xaml(.cs)              # Entry point, DI container, Client ID and language selection
├── MainWindow.xaml(.cs)       # Window: sign-in screen, NavigationView, player bar
├── Assets/                    # App icon (.ico / .png)
├── Controls/                  # SettingsCard: Windows 11 style settings row
├── Core/
│   ├── Api/                   # SpotifyApiClient: SpotifyAPI.Web wrapper plus direct Web API calls
│   ├── Auth/                  # PkceAuthService (PKCE sign-in), TokenStorage (DPAPI)
│   ├── Playback/              # Background Spotify client launch, playback transfer, SMTC bridge
│   ├── Settings/              # AppSettings / SettingsService (settings.json)
│   └── WindowMinSize.cs       # Minimum window size (WinAppSDK 1.6 has no built-in API for it)
├── Localization/              # Localizer (JSON strings, CLDR plural rules) and the {l:Tr} XAML extension
├── Strings/                   # Translations: <language code>.json, embedded into the app
├── ViewModels/                # MVVM with CommunityToolkit.Mvvm: Main, PlayerBar, Playlist, Search, Settings
├── Views/                     # Pages: Home, PlaylistDetail, Search, Settings, PlayerBar
└── Shared/                    # Shared WSHM shell types, vendored so the repo builds standalone
    ├── Diagnostics/           # AppLog: file logging
    └── Windows/               # FluentWindow, backdrops, themes, shell theme.json
```

**Main dependencies:** Microsoft.WindowsAppSDK 1.6, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, SpotifyAPI.Web, System.Security.Cryptography.ProtectedData.

</details>

## 💾 Where data is stored

| Data | Path |
|---|---|
| Settings (Client ID, language, backdrop, theme, background client) | `%LOCALAPPDATA%\FluentSpotify\settings.json` |
| Access tokens, encrypted with **DPAPI** for the current Windows user | `%LOCALAPPDATA%\FluentSpotify\tokens.dat` |
| Logs (once a log passes 1 MB, it is renamed to `.old`) | `%LOCALAPPDATA%\wshm\logs\fluent-spotify.log` |

To reset sign-in, sign out in Settings or delete `tokens.dat`.

## ⚖️ Disclaimer

This is an unofficial project. It is **not affiliated with, endorsed by or supported by Spotify AB**.
"Spotify" and the Spotify logo are trademarks of Spotify AB.
The app works only through the public [Spotify Web API](https://developer.spotify.com/documentation/web-api) and the official Spotify client, and its use is subject to the [Spotify Developer Terms](https://developer.spotify.com/terms).
