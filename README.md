# Scrlog

Запись экрана по расписанию для Windows 10/11 (.NET Framework 4.7.2 + ffmpeg).

- запись основного экрана в MKV (H.264), нарезка на файлы по N минут;
- автозапуск по расписанию, ручной старт/стоп, работа из трея;
- подсветка кликов мыши, курсор в записи;
- удаление записей старше N дней, копирование готовых файлов в сетевую папку.

Настройки и журнал работы: `C:\ProgramData\Scrlog`.

## Сборка установщика

Установщик собирается GitHub Actions ([.github/workflows/build.yml](.github/workflows/build.yml)) на каждый push
в `main` и в pull request — готовый `scrlog-setup-<версия>.exe` лежит в артефактах запуска (вкладка Actions).

Релиз: поставить тег с версией — установщик соберётся и будет приложен к GitHub Release.

```sh
git tag v2.0.3
git push origin v2.0.3
```

Установщик один для 32- и 64-битной Windows: он сам ставит ffmpeg нужной разрядности.

Локально (Windows): `dotnet build -c Release -o publish`, положить 64-битный `ffmpeg.exe` в `ffmpeg\x64\`
и 32-битный в `ffmpeg\x86\`, затем `ISCC.exe /DAppVersion=2.0.3 installer\scrlog.iss`.
