<p align="center">
  <img src="Assets/AppIcon.png" alt="Fluent Spotify" width="96" height="96" />
</p>

<h1 align="center">Fluent Spotify</h1>

<p align="center">
  Неофициальный клиент Spotify для Windows в стиле Fluent Design на WinUI 3
</p>

<p align="center">
  <img alt="Windows 10/11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white" />
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" />
  <img alt="WinUI 3" src="https://img.shields.io/badge/WinUI%203-Windows%20App%20SDK%201.6-005FB8" />
  <img alt="Spotify Web API" src="https://img.shields.io/badge/Spotify-Web%20API-1DB954?logo=spotify&logoColor=white" />
</p>

<p align="center">
  <b>Русский</b> · <a href="README.en.md">English</a>
</p>

<!-- Скриншот: положите файл в docs/screenshot.png и раскомментируйте строку ниже
<p align="center"><img src="docs/screenshot.png" alt="Скриншот Fluent Spotify" width="860" /></p>
-->

---

## ✨ Что это

**Fluent Spotify** — нативное приложение для Windows с интерфейсом в духе Windows 11: Mica, Acrylic, NavigationView и системная тема.
Медиатекой и воспроизведением оно управляет через **Spotify Web API**.

Само приложение звук не воспроизводит. Музыку играет **официальный клиент Spotify** как устройство Spotify Connect, а Fluent Spotify служит ему интерфейсом.
По умолчанию приложение само запускает официальный клиент **скрытым в фоне** и переносит на него воспроизведение, если сейчас ничего не играет.
Если музыка уже играет на другом устройстве, например на телефоне или другом ПК, приложение его не перехватывает.

## 🎧 Возможности

| | |
|---|---|
| 🔐 **Вход через PKCE** | Авторизация в браузере без client secret, токены хранятся в зашифрованном виде |
| 🏠 **Главная** | Ваши плейлисты и «Любимые треки» |
| 📃 **Плейлисты и любимые треки** | Треки подгружаются постепенно, страница за страницей, так что интерфейс не блокируется |
| 🔎 **Поиск** | Треки, исполнители и альбомы |
| ▶️ **Плеер** | Перемотка, громкость, перемешивание и повтор |
| ⌨️ **Медиаклавиши Windows** | Поддержка System Media Transport Controls (SMTC) |
| 🎨 **Оформление** | Материал окна (Mica, Mica Alt, Acrylic, Acrylic Thin или без материала) и тема (системная, светлая, тёмная) |
| 🐚 **Тема оболочки WSHM** | Если включить, материал и тема берутся из `~/.config/wshm/theme.json` и меняются вместе с оболочкой |
| 📐 **Адаптивная вёрстка** | Окно работает и в узком виде, минимальный размер 520×560 |

> [!NOTE]
> В режиме «Запускать Spotify в фоне» медиаэлементы Windows показывает сам официальный клиент. Встроенный мост SMTC приложения в этом режиме выключен, чтобы в системе не появлялось две карточки.

## 📋 Требования

- **Windows 10 1809** (сборка 17763) или новее, либо **Windows 11**, x64
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** для сборки
- **Официальный клиент Spotify** из [Microsoft Store](https://apps.microsoft.com/detail/9ncbcszsjrsb) или с [spotify.com](https://www.spotify.com/download/windows/)
- **Spotify Premium**: без него Web API не позволяет управлять воспроизведением

Отдельно устанавливать Windows App Runtime не нужно: проект собирается с `WindowsAppSDKSelfContained`, и среда выполнения Windows App SDK кладётся рядом с exe.

## 🔑 Настройка Spotify API

Приложению нужен собственный **Client ID**. Client secret не нужен, потому что используется PKCE.

1. Откройте [Spotify Developer Dashboard](https://developer.spotify.com/dashboard) и создайте приложение (**Create app**).
2. В поле **Redirect URIs** укажите адрес в точности так:

   ```text
   http://127.0.0.1:5000/callback/
   ```

   > [!IMPORTANT]
   > Spotify не принимает `localhost` в Redirect URI, поэтому нужен именно `127.0.0.1`. Завершающий `/` тоже важен.

3. В разделе **Which API/SDKs are you planning to use?** отметьте **Web API**.
4. Новое приложение работает в **Development Mode**. Добавьте свой аккаунт Spotify в **User Management** (имя и e-mail аккаунта), иначе вход не сработает.
5. Скопируйте **Client ID** (32 шестнадцатеричных символа) и передайте его приложению одним из способов:
   - введите его на экране входа и нажмите «Сохранить Client ID»;
   - задайте переменную окружения `FLUENT_SPOTIFY_CLIENT_ID`.

   Если заданы оба, приоритет у переменной окружения.

<details>
<summary><b>⚠️ Ограничения Development Mode</b></summary>

<br />

С февраля 2026 года Spotify ограничивает Web API для приложений в режиме разработки:

| Что | Ограничение |
|---|---|
| Содержимое плейлистов | Доступно только для плейлистов, которые вы создали или где вы соавтор. На чужих плейлистах Spotify возвращает **403**, и приложение показывает понятное сообщение |
| Поиск | Не больше **10** результатов на запрос |
| Пользователи | Войти могут только аккаунты из списка **User Management** |

Для приложений с расширенным доступом (Extended Quota) клиент тоже работает, потому что поддерживает и старые имена полей API.

</details>

## 🛠️ Сборка и запуск

```bash
git clone https://github.com/justaprunk/fluent-spotify.git
cd fluent-spotify
```

**Сборка:**

```bash
dotnet build -c Release -p:Platform=x64
```

Готовый `fluent-spotify.exe` будет в `bin\x64\Release\net8.0-windows10.0.22621.0\`.

**Публикация** в папку для распространения:

```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:Platform=x64
```

> [!TIP]
> С `--self-contained false` на целевой машине должна быть установлена [среда выполнения .NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

## 🗂️ Структура проекта

<details>
<summary>Развернуть</summary>

```text
fluent-spotify/
├── App.xaml(.cs)              # Точка входа, DI-контейнер, выбор Client ID
├── MainWindow.xaml(.cs)       # Окно: экран входа, NavigationView, плеер
├── Assets/                    # Иконка приложения (.ico / .png)
├── Core/
│   ├── Api/                   # SpotifyApiClient: обёртка над SpotifyAPI.Web и прямые запросы к Web API
│   ├── Auth/                  # PkceAuthService (вход через PKCE), TokenStorage (DPAPI)
│   ├── Playback/              # Запуск клиента Spotify в фоне, перенос воспроизведения, мост SMTC
│   ├── Settings/              # AppSettings / SettingsService (settings.json)
│   └── WindowMinSize.cs       # Минимальный размер окна (в WinAppSDK 1.6 его нет «из коробки»)
├── ViewModels/                # MVVM на CommunityToolkit.Mvvm: Main, PlayerBar, Playlist, Search, Settings
├── Views/                     # Страницы: Home, PlaylistDetail, Search, Settings, PlayerBar
└── Shared/                    # Общие типы оболочки WSHM, включены в репозиторий, чтобы он собирался сам по себе
    ├── Diagnostics/           # AppLog: файловый лог
    └── Windows/               # FluentWindow, материалы окна, темы, theme.json оболочки
```

**Основные зависимости:** Microsoft.WindowsAppSDK 1.6, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, SpotifyAPI.Web, System.Security.Cryptography.ProtectedData.

</details>

## 💾 Где хранятся данные

| Данные | Путь |
|---|---|
| Настройки (Client ID, материал окна, тема, фоновый клиент) | `%LOCALAPPDATA%\FluentSpotify\settings.json` |
| Токены доступа, зашифрованные **DPAPI** для текущего пользователя Windows | `%LOCALAPPDATA%\FluentSpotify\tokens.dat` |
| Логи (при размере больше 1 МБ старый лог переименовывается в `.old`) | `%LOCALAPPDATA%\wshm\logs\fluent-spotify.log` |

Чтобы сбросить вход, выйдите из аккаунта в настройках или удалите `tokens.dat`.

## ⚖️ Отказ от ответственности

Это неофициальный проект. Он **не связан со Spotify AB**, не одобрен и не поддерживается этой компанией.
«Spotify» и логотип Spotify являются товарными знаками Spotify AB.
Приложение работает только через публичный [Spotify Web API](https://developer.spotify.com/documentation/web-api) и официальный клиент Spotify, и при его использовании действуют [условия Spotify для разработчиков](https://developer.spotify.com/terms).
