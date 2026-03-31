# Contributing

Smart Filer is a personal project, but contributions are welcome.

## Development Setup

1. Install [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or .NET 9 SDK which includes 8.0 targeting)
2. Clone the repository
3. Open `SmartFiler.sln` in Visual Studio 2022 or Rider
4. Build: `dotnet build`
5. Run tests: `dotnet test`
6. Run the app: `dotnet run --project SmartFiler/SmartFiler.csproj`

## Architecture

- **MVVM pattern** using CommunityToolkit.Mvvm (source generators for ObservableProperty, RelayCommand)
- **DI container** via Microsoft.Extensions.DependencyInjection, wired in App.xaml.cs
- **SQLite** via Microsoft.Data.Sqlite with WAL mode for concurrent read/write
- **WPF** with ResourceDictionary-based theming (DynamicResource for runtime switching)
- **Services layer** -- all business logic in Services/, no logic in Views or code-behind

## Adding a New File Category

1. Add the enum value to `FileCategory` in `Data/Models.cs`
2. Add detection logic to `FileCategorizer.Categorize()` in `Services/FileCategorizer.cs`
3. Add a default destination to `CategoryDefaults` in `Services/SuggestionEngine.cs`
4. Add a filter checkbox to `MainViewModel` and `MainWindow.xaml`
5. Update `PassesFilter()` in `MainViewModel.cs`
6. Add tests to `SmartFiler.Tests/FileCategorizer_Tests.cs`

## Code Style

- C# 12 with file-scoped namespaces
- Records for data types, classes for services
- Async/await throughout (no .Result or .Wait())
- Parameterized SQL queries only (never interpolate user data into SQL)
