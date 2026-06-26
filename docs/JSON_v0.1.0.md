## 🧩 Универсальная модель S-выражения в JSON

В Lisp всё — это либо **атом** (число, строка, символ), либо **список** (последовательность элементов). Предлагаю отразить это в JSON с помощью явного поля `type`:

```json
{
  "type": "list",
  "items": [
    { "type": "symbol", "value": "net" },
    { 
      "type": "list",
      "items": [
        { "type": "symbol", "value": "name" },
        { "type": "string", "value": "GND" }
      ]
    },
    {
      "type": "list",
      "items": [
        { "type": "symbol", "value": "node" },
        { "type": "list", "items": [ { "type": "symbol", "value": "ref" }, { "type": "string", "value": "C1" } ] },
        { "type": "list", "items": [ { "type": "symbol", "value": "pin" }, { "type": "number", "value": 1 } ] }
      ]
    }
  ]
}
```

### Классы C# для такой модели:

```csharp
public abstract class SExpression { }

public class SAtom : SExpression
{
    public string Type { get; set; } // "symbol", "string", "number", "boolean"
    public object Value { get; set; }
}

public class SList : SExpression
{
    public List<SExpression> Items { get; set; } = new();
}
```

### Преимущества:
- **Полная гибкость** – можно представить любое S-выражение.
- **Простота сериализации** – встроенный `System.Text.Json` справится без проблем.
- **Обратимость** – легко преобразовать обратно в текст S-выражения (если нужно).

### Недостатки:
- **Нетипизированность** – вам придётся вручную обходить дерево и искать нужные узлы (например, найти все `node` внутри `net`). Это может быть утомительно и чревато ошибками.
- **Избыточность** – при хранении всего нетлиста вы получите большой объём служебных полей (`type`, `items`).

---

## 🎯 Предпочтительный вариант: предметно-ориентированная модель

Я бы рекомендовал **не использовать** универсальный S-выражение для внутреннего хранения, а создать **строгую предметную модель** `NetlistDocument`, которая отражает структуру нетлиста. Её же можно сериализовать в JSON с более осмысленными именами полей:

```json
{
  "sourceFileName": "myboard.net",
  "format": "KiCad",
  "parsedAt": "2025-01-15T12:00:00Z",
  "components": [
    { "ref": "C1", "value": "100nF", "footprint": "0805" },
    { "ref": "R1", "value": "10k", "footprint": "0805" }
  ],
  "nets": [
    {
      "name": "GND",
      "pins": [
        { "componentRef": "C1", "pin": 1 },
        { "componentRef": "R1", "pin": 2 }
      ]
    }
  ]
}
```

### Классы C#:

```csharp
public class NetlistDocument
{
    public string SourceFileName { get; set; }
    public string Format { get; set; }
    public DateTime ParsedAt { get; set; }
    public List<Component> Components { get; set; } = new();
    public List<Net> Nets { get; set; } = new();
}

public class Component
{
    public string Ref { get; set; }
    public string Value { get; set; }
    public string Footprint { get; set; }
}

public class Net
{
    public string Name { get; set; }
    public List<PinConnection> Pins { get; set; } = new();
}

public class PinConnection
{
    public string ComponentRef { get; set; }
    public string Pin { get; set; } // может быть строкой (например, "A", "B") или числом
}
```

### Почему это лучше?

- **Читаемость и понятность** – сразу видно, что это компоненты и сети.
- **Удобство работы** – вы можете обращаться к `document.Nets.First(n => n.Name == "GND")` без обхода дерева.
- **Безопасность типов** – компилятор проверит корректность полей.
- **Меньше объём** – нет лишних полей `type` и `items`.
- **Легко расширять** – при добавлении новых полей (например, `vendor`) вы просто добавляете свойство.

---

## 🔄 Гибридный подход (на всякий случай)

Если вы всё же хотите оставить возможность хранить S-выражения в JSON (например, для отладки или для будущих форматов), можно сделать **два уровня**:

1. **Парсер** преобразует S-выражение в `NetlistDocument`.
2. **Сериализатор** может сохранять `NetlistDocument` в JSON (предметный).
3. **Опционально** – сохранять сырое S-выражение как строку в поле `RawSExpression` для отладки.

Это даёт вам и гибкость, и удобство.

---

## 📌 Итоговая рекомендация

**Используйте предметно-ориентированную модель `NetlistDocument`** как ваш внутренний формат. Она реализует всю логику S-листа (иерархию, вложенность) на уровне объектов, но в гораздо более удобной для .NET форме. Сериализуйте её в JSON для кеширования, обмена или отладки.

Если позже возникнет необходимость работать с произвольными S-выражениями (например, для поддержки других форматов), вы всегда сможете ввести отдельный универсальный слой, но для текущей задачи он избыточен.

---

## 🛠️ Реализация в рамках этапа 9

Включите в этап 9:

- Создание классов модели в `Core`.
- Реализацию `JsonNetlistSerializer` с использованием `System.Text.Json`.
- Модификацию парсеров для выдачи `NetlistDocument`.
- Сохранение JSON-файла (по желанию) в папку `out` рядом с другими результатами.

