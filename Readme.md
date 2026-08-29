dotnet new sln -n Project1

把后端项目加入 solution
dotnet sln Project1.slnx add backend/Project1.Api/Project1.Api.csproj

start up backend
dotnet run --project backend/Project1.Api
dotnet watch --project backend/Project1.Api

install entity framework
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 10.0.10

dotnet add backend/Project1.Api/Project1.Api.csproj package Microsoft.EntityFrameworkCore.Design --version 10.0.10

EF Core 会比较
当前 C# Model
vs
上一次 ModelSnapshot
然后计算数据库需要发生什么变化。
dotnet tool run dotnet-ef migrations add xxx `
  --project backend/Project1.Api `
  --startup-project backend/Project1.Api


dotnet tool run dotnet-ef database update `
  --project backend/Project1.Api `
  --startup-project backend/Project1.Api


database update
→ 前进到最新版本，执行 Up()

database update <旧 Migration>
→ 回退到指定版本，执行较新 Migration 的 Down()

database update 0
→ 回退所有 Migration
