# PAD — архитектура (Фаза 1)

## 1. Анализ референсов → дизайн-система

Референсы: visionOS (панели Apple Music, Siri, виджет Календаря), macOS Big Sur (Messages/Safari/Control Center), macOS Monterey (галерея «Команды»).

Что берём:
- **Матовое стекло (acrylic/mica)**: полупрозрачные панели поверх мягкого фона, тонкая светлая граница 1px, мягкая тень. В WPF: Mica/Acrylic через `DwmSetWindowAttribute` (Win11) + fallback на сплошной полупрозрачный цвет (Win10).
- **Узкая иконочная боковая панель** слева (visionOS: колонка иконок; Big Sur: сайдбар со списком). Режим: свёрнутая 56px / развёрнутая 220px.
- **Плавающая нижняя панель управления** (плеер visionOS) → паттерн для панели быстрых действий и статуса автосохранения.
- **Цветные плитки с иконкой и подписью** (галерея «Команды») → карточки показателей и шаблонов; цвет кодирует смысл (доход/ожидание/просрочка).
- **Настройки-панель**: переключатель, слайдер, палитра цветов (виджет Календаря) → экран настроек и выбор акцентного цвета.
- **Типографика**: гротеск уровня Segoe UI Variable (Win11) / Segoe UI (Win10); заголовки 20/16, текст 13, подписи 11; цифры — табличные (`Typography.NumeralAlignment=Tabular`) для денежных колонок.

Что сознательно НЕ берём (по ТЗ): крупные скругления visionOS (радиус ограничиваем 8px карточки / 6px поля / 4px кнопки), декоративные градиенты, огромные карточки. Плотность — «рабочая», как у настольных профессиональных инструментов.

Токены: `Color.Surface0..3`, `Color.Accent`, `Color.Income` (зелёный), `Color.Pending` (янтарный), `Color.Overdue` (красный), `Space.4/8/12/16/24`, `Radius.S/M`, светлая и тёмная темы через `ResourceDictionary`.

## 2. Стек и слои

```
PAD.sln
  PAD.Domain         — сущности, enum'ы, FinanceCalculator (чистая логика, без зависимостей)
  PAD.Data           — EF Core + SQLite, миграции, репозитории, транзакции, бэкап
  PAD.Application    — сервисы: ProjectService, PaymentService, DeliveryService, AnalyticsService, SearchService
  PAD.App            — WPF, MVVM (CommunityToolkit.Mvvm), DI (Microsoft.Extensions.DependencyInjection)
  PAD.Tests          — xUnit: финансы, целостность, бэкап/восстановление
```
Графики: LiveCharts2 (LiveChartsCore.SkiaSharpView.WPF) — бесплатная, офлайн. Markdown-редактор: AvalonEdit + Markdig для превью. XLSX: ClosedXML (опционально), CSV — вручную.
Установщик: Inno Setup (бесплатный) или WiX; сборка `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`.

## 3. Ключевые архитектурные решения

1. **Деньги — `decimal`**. В SQLite EF Core хранит decimal как TEXT; суммы и агрегации делаем через конвертер в **целые минорные единицы (long, центы)** — `ValueConverter<Money,long>`, чтобы SUM/сортировка в SQL работали точно.
2. **Статус сдачи ≠ статус оплаты.** `Project.Status` (жизненный цикл) и `PaymentStatus` (вычисляется из платежей, не хранится) независимы. `ProjectDelivery` — отдельная запись с историей (повторное открытие создаёт новую запись, старая получает `ReopenedAt`).
3. **Заработок = только `Payment` с `IsReceived = true`**, по `ReceivedDate`. Стоимость заказа никогда не попадает в «заработано».
4. **Возвраты** — платёж типа `Refund` с отрицательным вкладом; чистая сумма = Σ(получено) − Σ(возвраты).
5. **Идемпотентность платежей**: уникальный индекс `(ProjectId, ExternalRef)` + `RowVersion`; всё в `BeginTransaction`.
6. **Мультивалютность**: итоги группируются по `Currency`; пересчёт — только по ручному курсу, сохранённому в платеже (`ManualRate`, `RateToCurrency`). Выдуманных курсов нет.
7. **История**: `ProjectPriceHistory`, `ProjectStatusHistory`, `StageStatusHistory` — append-only.
8. **Архив**: флаг `IsArchived`; платежи не трогаются, аналитика считает по ним независимо от архивации.
9. **Вложения**: хранятся в `%LOCALAPPDATA%\PAD\files\<sha256>`; `Attachment` — ссылка + счётчик ссылок; физический файл удаляется только когда ссылок 0.
10. **Надёжность**: SQLite WAL + `PRAGMA foreign_keys=ON`, `synchronous=FULL`; автобэкап перед миграцией; бэкап = `VACUUM INTO` + zip с файлами; восстановление через временную папку и атомарную подмену.
11. **Автосохранение** редакторов: debounce 800 мс + сохранение при потере фокуса и закрытии.

## 4. Модель данных (основное)

Project(Id, Name, Description, Type→Category, ClientName, ClientContact, Goal, Priority, Status, Price, Currency, StartPlanned, Deadline, PaymentTerms, PrepaymentAmount, FinanceNotes, IsArchived, CreatedAt)
ProjectStage(Id, ProjectId, Order, Title, Status, ContentMarkdown) → StageSection(Id, StageId, Order, Title, ContentMarkdown)
Payment(Id, ProjectId, Amount, Currency, ReceivedDate, Type, Purpose, Comment, IsReceived, ManualRate?, RateCurrency?)
PaymentSchedule(Id, ProjectId, Amount, Currency, DueDate, Note)
ProjectDelivery(Id, ProjectId, DeliveredAt, FinalPrice, Currency, ProductUrl, Comment, HandoverResult, ReopenedAt?)
Task(+TaskChecklistItem), Note(+Tag), Attachment, CalendarEvent, ProjectTemplate(+TemplateStage), UserProfile, ApplicationSettings, Category, Tag, Currency.

## 5. План и честный статус

| Фаза | Статус |
|---|---|
| 1 Анализ и архитектура | сделано (этот файл) |
| 2 Фундамент | начато: Domain + FinanceCalculator + тесты |
| 3–9 | не начаты; требуют Windows + .NET SDK для сборки и проверки |

Все пункты «запусти и проверь» из ТЗ (разд. 20–21) выполнимы только на Windows. Код, написанный мной без компиляции, нужно прогонять через `dotnet build` и `dotnet test` сразу после каждой фазы.
