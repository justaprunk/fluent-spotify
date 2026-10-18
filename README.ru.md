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
  <a href="README.md">English</a> · <b>Русский</b>
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

| Возможность | Описание |
|---|---|
| 🔐 **Вход через PKCE** | Авторизация в браузере без client secret, токены хранятся в зашифрованном виде |
| 🏠 **Главная** | Ваши плейлисты и «Любимые треки» |
| 📃 **Плейлисты и любимые треки** | Треки подгружаются постепенно, страница за страницей, так что интерфейс не блокируется |
| 📊 **Мои итоги** | Топ треков, артистов и жанров за год, 6 месяцев или 4 недели — как Spotify Wrapped. Итоги можно сохранить в приватный плейлист |
| 🎶 **Панель «Сейчас играет»** | Правая панель, как в официальном приложении: контекст воспроизведения, крупная обложка, отметка «Нравится», синхронизированный текст песни, исполнитель с краткой биографией, исполнители трека с подпиской и ближайшие треки очереди. На узком окне сворачивается |
| 🔎 **Поиск** | Треки, исполнители и альбомы |
| ▶️ **Плеер** | Перемотка, громкость, перемешивание и повтор |
| ⌨️ **Медиаклавиши Windows** | Поддержка System Media Transport Controls (SMTC) |
| 🎨 **Оформление** | Материал окна (Mica, Mica Alt, Acrylic, Acrylic Thin или без материала) и тема (системная, светлая, тёмная) |
| 🐚 **Тема оболочки WSHM** | Если включить, материал и тема берутся из `~/.config/wshm/theme.json` и меняются вместе с оболочкой |
| 🌐 **15 языков интерфейса** | Английский, французский, немецкий, нидерландский, японский, корейский, польский, португальский (Бразилия), русский, китайский (упрощённый и традиционный), испанский (Латинская Америка и Испания), турецкий, украинский. По умолчанию — язык Windows, другой можно выбрать в настройках |
| 📐 **Адаптивная вёрстка** | Окно работает и в узком виде, минимальный размер 520×560 |

> [!NOTE]
> В Spotify Web API нет текстов песен, биографий исполнителей, авторов песен и числа слушателей. Тексты в панели «Сейчас играет» берутся из стороннего сервиса [LRCLIB](https://lrclib.net), биография — из Википедии (статья ищется через Wikidata по Spotify ID исполнителя). И то и другое можно отключить в настройках. Для отметок «Нравится» и подписок нужны новые разрешения, поэтому после обновления приложение один раз попросит войти заново.

> [!NOTE]
> В режиме «Запускать Spotify в фоне» медиаэлементы Windows показывает сам официальный клиент. Встроенный мост SMTC приложения в этом режиме выключен, чтобы в системе не появлялось две карточки.

## ⬇️ Скачать

Последний установщик лежит в **[Releases](https://github.com/justaprunk/fluent-spotify/releases/latest)**: `FluentSpotify-<версия>-Setup-x64.exe`.

- Ставится для текущего пользователя в `%LOCALAPPDATA%\Programs\Fluent Spotify`, права администратора не нужны.
- Среда .NET и Windows App SDK уже внутри, отдельно ничего ставить не нужно.
- Создаёт ярлык в меню «Пуск» (и, по желанию, на рабочем столе). Удаление — через **Параметры → Приложения**.

> [!NOTE]
> Установщик пока не подписан, поэтому Windows SmartScreen может показать предупреждение: нажмите **Подробнее → Выполнить в любом случае**. Файл можно сверить с контрольной суммой `.sha256` из релиза.
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
| Жанры исполнителей | Поле устарело и часто приходит пустым, поэтому топ жанров в «Моих итогах» может быть недоступен |

Для приложений с расширенным доступом (Extended Quota) клиент тоже работает, потому что поддерживает и старые имена полей API.

</details>

## 🛠️ Сборка и запуск

```powershell
# 1. Клонировать репозиторий
git clone https://github.com/justaprunk/fluent-spotify.git
cd fluent-spotify

# 2. Собрать (Release, x64)
dotnet build -c Release -p:Platform=x64

# 3. Опубликовать в папку для распространения (необязательно)
dotnet publish -c Release -r win-x64 --self-contained false -p:Platform=x64
```

После сборки `fluent-spotify.exe` лежит в `bin\x64\Release\net8.0-windows10.0.22621.0\`.

Чтобы собрать установщик самостоятельно (нужен [Inno Setup 6](https://jrsoftware.org/isinfo.php): `winget install JRSoftware.InnoSetup`):

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

Скрипт публикует self-contained сборку и кладёт в `installer\Output\` файл `FluentSpotify-<версия>-Setup-x64.exe`, его `.sha256` и `.sha256.sig`. Если у вас нет ключа подписи владельца, добавьте `-Unsigned`: установщик будет работать, но встроенный апдейтер его не примет.

### Релизы и подпись обновлений

Встроенный апдейтер ставит релиз, только если тот подписан офлайн-ключом владельца. Поэтому даже взломанный аккаунт GitHub не позволит разослать обновление пользователям.

- **Ключ.** ECDSA P-256, создаётся один раз: `installer\signing-key.ps1 -New`. Секретная часть лежит вне репозитория в `%USERPROFILE%\.fluent-spotify\update-signing-key.dpapi`. Она зашифрована DPAPI текущего пользователя Windows, и доступ по NTFS есть только у него. Открытый ключ (SubjectPublicKeyInfo, base64) встроен в `Core/Updates/UpdateSignature.cs`. Резервная копия: `pwsh -File installer\signing-key.ps1 -Export <файл>` (PKCS#8 под паролем), восстановление: `-Import <файл>`. В git-репозиторий и синхронизируемые с облаком папки скрипт копии не пишет.
- **Резервный ключ.** Второй ключ создаётся командой `pwsh -File installer\signing-key.ps1 -NewRecovery <путь на флешке>` и существует только как зашифрованный паролем файл на офлайн-флешке. Его открытая часть встроена рядом с основной, и приложение принимает подпись любым из двух ключей. Если основной ключ потерян или утёк, подпишите релиз командой `-SignWith <recovery.pem> -File <установщик>.sha256` и замените в этом релизе список ключей. **Если потеряны оба ключа, установленные копии больше не обновятся, и пользователям придётся переустановить приложение вручную.**
- **Подпись.** `build-installer.ps1` подписывает ECDSA/SHA-256 точные байты `<установщик>.sha256`, где записаны хеш и имя файла с версией. Файл `<установщик>.sha256.sig` содержит одну строку: подпись в base64, формат IEEE P1363 (`r || s`, 64 байта).
- **Проверка в приложении.** `UpdateService` предлагает только релизы, где все три файла лежат на `github.com/justaprunk/fluent-spotify/releases/download/`. Перед запуском установщика он делает три проверки:
  1. подпись `.sha256`;
  2. совпадение подписанного имени и версии с релизом — так старую подписанную сборку нельзя перевыложить как новую;
  3. SHA-256 скачанного установщика через дескриптор, закрытый для записи.
- **Выпуск релиза.** Поднимите версию в `fluent-spotify.csproj` и `installer\FluentSpotify.iss`, запустите `build-installer.ps1` и приложите к релизу на GitHub все три файла из `installer\Output\`: `.exe`, `.exe.sha256` и `.exe.sha256.sig`.
- **Переход.** Релизы до 1.3.2 включительно не подписаны. Их апдейтер подпись не проверяет и поставит 1.3.3 как обычно. Начиная с 1.3.3 неподписанные релизы приложение игнорирует.
- **Тестирование (только Debug-сборка).** `FLUENT_SPOTIFY_UPDATE_FEED=<url>` подменяет API релизов GitHub своим списком в том же формате. Файлы тогда можно отдавать с того же адреса, но подписывать их всё равно нужно встроенным ключом.

> [!TIP]
> С `--self-contained false` на целевой машине должна быть установлена [среда выполнения .NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

## 🗂️ Структура проекта

<details>
<summary>Развернуть</summary>

```text
fluent-spotify/
├── App.xaml(.cs)              # Точка входа, DI-контейнер, выбор Client ID и языка
├── MainWindow.xaml(.cs)       # Окно: экран входа, NavigationView, плеер
├── Assets/                    # Иконка приложения (.ico / .png)
├── Controls/                  # SettingsCard: строка настроек в стиле Windows 11
├── Core/
│   ├── Api/                   # SpotifyApiClient: обёртка над SpotifyAPI.Web и прямые запросы к Web API
│   ├── NowPlaying/            # LyricsService (LRCLIB), ArtistBioService (Wikidata + Википедия)
│   ├── Auth/                  # PkceAuthService (вход через PKCE), TokenStorage (DPAPI)
│   ├── Playback/              # Запуск клиента Spotify в фоне, перенос воспроизведения, мост SMTC
│   ├── Settings/              # AppSettings / SettingsService (settings.json)
│   └── WindowMinSize.cs       # Минимальный размер окна (в WinAppSDK 1.6 его нет «из коробки»)
├── Localization/              # Localizer (строки из JSON, правила множественного числа CLDR) и расширение {l:Tr} для XAML
├── Strings/                   # Переводы: <код языка>.json, встроены в приложение
├── ViewModels/                # MVVM на CommunityToolkit.Mvvm: Main, PlayerBar, NowPlaying, Playlist, Search, Settings
├── Views/                     # Страницы: Home, PlaylistDetail, Search, Settings, PlayerBar, NowPlaying
└── Shared/                    # Общие типы оболочки WSHM, включены в репозиторий, чтобы он собирался сам по себе
    ├── Diagnostics/           # AppLog: файловый лог
    └── Windows/               # FluentWindow, материалы окна, темы, theme.json оболочки
```

**Основные зависимости:** Microsoft.WindowsAppSDK 1.6, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, SpotifyAPI.Web, System.Security.Cryptography.ProtectedData.

</details>

## 💾 Где хранятся данные

| Данные | Путь |
|---|---|
| Настройки (Client ID, язык, материал окна, тема, фоновый клиент) | `%LOCALAPPDATA%\FluentSpotify\settings.json` |
| Токены доступа, зашифрованные **DPAPI** для текущего пользователя Windows | `%LOCALAPPDATA%\FluentSpotify\tokens.dat` |
| Логи (при размере больше 1 МБ старый лог переименовывается в `.old`) | `%LOCALAPPDATA%\wshm\logs\fluent-spotify.log` |

Чтобы сбросить вход, выйдите из аккаунта в настройках или удалите `tokens.dat`.

## ⚖️ Отказ от ответственности

Это неофициальный проект. Он **не связан со Spotify AB**, не одобрен и не поддерживается этой компанией.
«Spotify» и логотип Spotify являются товарными знаками Spotify AB.
Приложение работает только через публичный [Spotify Web API](https://developer.spotify.com/documentation/web-api) и официальный клиент Spotify, и при его использовании действуют [условия Spotify для разработчиков](https://developer.spotify.com/terms).
