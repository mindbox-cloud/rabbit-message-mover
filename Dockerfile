FROM mcr.microsoft.com/dotnet/core/sdk:3.0-buster AS build

WORKDIR /src
COPY ["RabbitMessageMover/RabbitMessageMover.csproj", "./"]
RUN dotnet restore

COPY . ./

RUN dotnet build -c Release

RUN dotnet test 

RUN dotnet publish ./RabbitMessageMover/RabbitMessageMover.csproj --no-build -o ./out


FROM mcr.microsoft.com/dotnet/core/runtime:3.0
WORKDIR /app
COPY --from=build /src/out .
CMD ["dotnet", "RabbitMessageMover.dll"]