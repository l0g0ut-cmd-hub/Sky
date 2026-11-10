# Небесный прилив — прототип 0.1

Минимальный прототип: воздушный корабль над морем закатных облаков, джойстик и камера вокруг корабля.

Unity **6000.0.36f1**, встроенный рендер (шейдер Standard), Android (IL2CPP, ARM64), горизонтальная ориентация.

## Управление
| Где | Что делает |
|---|---|
| Левая половина экрана | Плавающий джойстик (появляется под пальцем): вверх — ход вперёд, вниз — тормоз/назад, в стороны — поворот |
| Правая половина экрана | Вращать камеру вокруг корабля (вбок и вверх-вниз) |
| Два пальца справа | Приблизить / отдалить |
| Редактор: WASD / стрелки, ЛКМ, колесо | То же с клавиатуры и мыши |

## Как открыть в Unity
1. Открыть папку репозитория в Unity Hub (версия 6000.0.36f1).
2. При первом открытии сцена `Assets/_Project/Scenes/SkyTide_Prototype.unity` соберётся сама (или меню **Sky Tide → Собрать сцену прототипа**).
3. Play.

Если в консоли ошибка про Input Manager: Project Settings → Player → Active Input Handling = **Input Manager (Old)** или **Both**.

Сцена и материалы в репозитории не хранятся — их создаёт `Editor/SceneBuilder.cs`. Всё остальное (свет, камера, облака, корабль, управление) собирается при запуске в `Core/SkyTideBootstrap.cs`.

## Модель корабля
Пока корабль — заглушка из примитивов по концепту (`Ship/ShipFactory.cs`).
Чтобы подключить FBX: положить файл как `Assets/_Project/Models/Ship.fbx` и пересобрать сцену (меню Sky Tide).
Модель масштабируется под длину `shipModelLength` (38 м); если нос смотрит не туда — поправить `shipModelEuler` на объекте SkyTide.
Тяжёлый FBX (больше 100 МБ) в обычный git не влезет — нужен Git LFS.

## Сборка APK (GitHub Actions + GameCI)
Workflow `.github/workflows/build-android.yml`. Нужны секреты `UNITY_EMAIL` и `UNITY_PASSWORD`.
- Запуск: Actions → **Build Android APK** → Run workflow, либо пуш в ветку `apk-build`.
- Готовый APK: артефакт `SkyTide-Android-APK` в запуске, а также ветка `apk-latest` (файл `SkyTide.apk`).
- Лог Unity (без секретов): ветка `ci-logs`, `logs/run-N.log`.

## Структура
```
Assets/_Project/Scripts/
  Core/       SkyTideBootstrap (точка входа), GameMaterials, MaterialFactory
  Ship/       ShipController (ход, поворот, крен, покачивание), ShipFactory (сборка корабля)
  CameraRig/  OrbitCamera
  Input/      TouchControls (джойстик + вращение камеры)
  World/      CloudSea (море облаков, облачные гряды, скалы с замками)
  Editor/     SceneBuilder, BuildScript
```
