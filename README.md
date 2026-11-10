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
Корабль — модель Meshy «Skyward Corsair», упрощённая с 3,4 млн до ~170 тыс. треугольников для телефона.
Конвертация GLB → проект одной командой (нужны numpy, Pillow, opencv):
```
python3 tools/convert_glb.py model.glb --name Corsair --length 38
```
Пишет `Assets/_Project/Models/Corsair.bytes` (меш уже в координатах Unity, нос в +Z) и текстуры Albedo / Normal / Emission
(эмиссия — маска горящих окон и фонарей). Импортёр GLB в Unity не нужен. Параметр `--cell` — степень упрощения
(больше → меньше треугольников), `--nose` — куда смотрит нос в исходнике (у Meshy `-x`).
Если файлов модели нет, корабль собирается из примитивов (`Ship/ShipFactory.cs`).

## Небо и облака
`Assets/_Project/Art/`: `SkyPanorama.jpg` (панорама 6144×3072, сшита из 4 кусков tools/stitch_sky.py, солнце в центре = направление +X, по нему выставлен свет),
`CloudSea.jpg` (бесшовный тайл моря облаков), `CloudLayer.png` (тот же тайл с прозрачностью — верхний слой),
`CloudSprites.png` (атлас из 6 облаков; прямоугольники в `World/CloudSea.cs`). Цвета тумана и фонового света сняты с панорамы.

## Сборка APK (GitHub Actions + GameCI)
Workflow `.github/workflows/build-android.yml`. Нужны секреты `UNITY_EMAIL` и `UNITY_PASSWORD`.
- Запуск: Actions → **Build Android APK** → Run workflow, либо пуш в ветку `apk-build`.
- Готовый APK: артефакт `SkyTide-Android-APK` в запуске, а также ветка `apk-latest` (файл `SkyTide.apk`).
- Лог Unity (без секретов): ветка `ci-logs`, `logs/run-N.log`.

## Структура
```
Assets/_Project/Scripts/
  Core/       SkyTideBootstrap (точка входа), GameMaterials, MaterialFactory
  Ship/       ShipController (ход, поворот, крен, покачивание), ShipFactory (сборка корабля), ShipMeshLoader
  CameraRig/  OrbitCamera
  Input/      TouchControls (джойстик + вращение камеры)
  World/      CloudSea (море облаков, облака-спрайты, скалы с замками)
tools/        convert_glb.py (GLB → меш и текстуры для проекта)
  Editor/     SceneBuilder, BuildScript
```
