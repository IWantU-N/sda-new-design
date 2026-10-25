# SDA Desk

[Русский](#русский) | [English](#english)

---

## Русский

SDA Desk — настольное приложение для Windows, которое помогает управлять Steam Mobile Authenticator и подтверждениями Steam. Интерфейс и приложение самостоятельные; проект вдохновлён сценариями [Steam Desktop Authenticator](https://github.com/jessecar96/steamdesktopauthenticator), но не является его официальной версией и не связан с Valve или Steam.

### Возможности

- Импорт существующих открытых `.maFile` и JSON-файлов.
- Создание `.maFile` из уже имеющихся данных аутентификатора.
- Привязка мобильного аутентификатора через Steam: вход, Steam Guard-коды из почты или приложения, запрос SMS/номера при необходимости и ввод revocation code.
- Генерация и копирование кодов Steam Guard.
- Просмотр подтверждений обменов, продаж и покупок Community Market для выбранных аккаунтов; подтверждение или отклонение выбранных действий.
- Экспорт открытого `.maFile` для резервного копирования.
- Локальное хранение аккаунтов в папке `maFiles` рядом с приложением.

### Производительность

В SDA Desk обработка запросов заявлена до 5 раз быстрее по сравнению с оригинальным Steam Desktop Authenticator. Фактическая скорость зависит от компьютера, сети, ответа Steam и типа запроса; результат может отличаться.

### Безопасность и данные

Файлы аутентификатора сохраняются **без шифрования**. Они содержат секреты, позволяющие генерировать коды и выполнять подтверждения. Храните папку приложения в безопасном месте, делайте резервные копии и никогда не публикуйте `.maFile`, `identity_secret`, `shared_secret`, токены сессии или содержимое папок `maFiles` и `pending`.

Пароль Steam используется во время входа и не сохраняется приложением. Steam может запросить код из почты, текущий Steam Guard-код, SMS или подтверждение номера телефона. Привязка завершается только после успешной активации и ввода revocation code.

Во время активации временные данные могут находиться в незашифрованном виде в папке `pending` рядом с приложением. Не удаляйте их, пока не проверите состояние аутентификатора в Steam, если процесс активации был прерван. Готовые файлы находятся в `maFiles`.

Подтверждения требуют `.maFile` с `identity_secret`, `device_id` и действующей сессией. Перед подтверждением проверьте аккаунт и детали операции. Массовые действия выполняются только после явного выбора пользователем.

### Сборка и запуск

Требуются Windows и .NET 10 SDK. Для восстановления зависимостей нужен доступ к NuGet.

```powershell
dotnet restore
dotnet run
```

Сборка Release:

```powershell
dotnet build -c Release
```

### Лицензия и сторонний код

Код SteamAuth включён из [geel9/SteamAuth](https://github.com/geel9/SteamAuth). Его лицензия находится в [`lib/SteamAuth/LICENSE`](lib/SteamAuth/LICENSE). Проверьте условия этой лицензии перед распространением сборок.

---

## English

SDA Desk is a Windows desktop application for managing Steam Mobile Authenticator accounts and Steam confirmations. It has its own interface and implementation. The project is inspired by workflows in [Steam Desktop Authenticator](https://github.com/jessecar96/steamdesktopauthenticator); it is not an official release and is not affiliated with Valve or Steam.

### Features

- Import existing unencrypted `.maFile` and JSON files.
- Create an `.maFile` from authenticator data you already have.
- Link a mobile authenticator through Steam, including sign-in, email or app Steam Guard codes, SMS/phone prompts when required, and revocation-code entry.
- Generate and copy Steam Guard codes.
- View trade, sale, and Community Market purchase confirmations for selected accounts, then confirm or reject selected items.
- Export an unencrypted `.maFile` for backup.
- Store accounts locally in the `maFiles` folder beside the application.

### Performance

SDA Desk is claimed to process requests up to 5× faster than the original Steam Desktop Authenticator. Actual speed depends on the computer, network, Steam response time, and request type; results may vary.

### Security and data

Authenticator files are stored **without encryption**. They contain secrets that can generate codes and authorize confirmations. Keep the application folder secure, maintain backups, and never publish `.maFile` files, `identity_secret`, `shared_secret`, session tokens, or the contents of `maFiles` and `pending` folders.

Your Steam password is used during sign-in and is not saved by the application. Steam may request an email code, a current Steam Guard code, an SMS code, or phone-number verification. Linking is complete only after activation succeeds and the revocation code is entered.

During activation, temporary data may be stored unencrypted in the `pending` folder beside the application. If activation is interrupted, do not delete it until you have checked the authenticator status in Steam. Completed files are stored in `maFiles`.

Confirmations require an `.maFile` with `identity_secret`, `device_id`, and a valid session. Check the account and operation details before confirming. Bulk actions are sent only after the user explicitly selects them.

### Build and run

Requires Windows and the .NET 10 SDK. NuGet access is needed to restore dependencies.

```powershell
dotnet restore
dotnet run
```

Build Release:

```powershell
dotnet build -c Release
```

### License and third-party code

SteamAuth code is included from [geel9/SteamAuth](https://github.com/geel9/SteamAuth). Its license is available at [`lib/SteamAuth/LICENSE`](lib/SteamAuth/LICENSE). Review that license before distributing builds.

