```text
NetFileConverter/
├── NetFileConverter.Core/               (Class Library)
│   ├── Models/
│   │   ├── NetlistDocument.cs
│   │   ├── Component.cs
│   │   ├── Net.cs
│   │   └── PinConnection.cs
│   ├── Interfaces/
│   │   ├── INetlistParser.cs
│   │   ├── IOutputGenerator.cs
│   │   └── INetlistSerializer.cs
│   └── Serialization/
│       └── JsonNetlistSerializer.cs
├── NetFileConverter.Infrastructure/     (Class Library)
│   ├── Parsers/
│   │   ├── Protel2Parser.cs
│   │   └── KiCadParser.cs
│   └── Generators/
│       ├── NetlistGenerator.cs
│       ├── BomGenerator.cs
│       ├── DotGenerator.cs
│       └── MermaidGenerator.cs
├── NetFileConverter.App/                (Windows Application)
│   ├── Program.cs
│   ├── Worker.cs
│   ├── SettingsForm.cs
│   └── appsettings.json
└── NetFileConverter.Core.Tests/         (xUnit Test Project)
    └── SerializationTests.cs
```