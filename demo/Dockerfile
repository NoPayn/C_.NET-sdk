FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/NoPayn/NoPayn.csproj src/NoPayn/
COPY demo/demo.csproj demo/
RUN dotnet restore demo/demo.csproj

COPY src/ src/
COPY demo/ demo/
RUN dotnet publish demo/demo.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .

EXPOSE 3000
ENV ASPNETCORE_URLS=http://+:3000

ENTRYPOINT ["dotnet", "demo.dll"]
