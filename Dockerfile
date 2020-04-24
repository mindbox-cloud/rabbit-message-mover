FROM mcr.microsoft.com/dotnet/core/sdk:3.0-buster AS build

# prepare sources
WORKDIR /src
COPY [".", "."]

# build & test
RUN dotnet restore "RabbitMessageMover.sln"
RUN dotnet build -c Release --no-restore --
RUN dotnet test 

# publish
RUN dotnet publish "./RabbitMessageMover/RabbitMessageMover.csproj" --output "/build/RabbitMessageMover" -c Release --no-restore


# create app cntnr
FROM mcr.microsoft.com/dotnet/core/runtime:3.0
WORKDIR /app
COPY --from=build /build/RabbitMessageMover .
CMD ["dotnet", "RabbitMessageMover.dll"]
