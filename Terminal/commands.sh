dotnet nuget locals --clear all
rd /s /q .\bin
rd /s /q .\obj
dotnet restore
dotnet build -c Release