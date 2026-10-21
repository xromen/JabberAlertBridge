FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json .editorconfig ./
COPY src/JabberAlertBridge/JabberAlertBridge.csproj src/JabberAlertBridge/packages.lock.json src/JabberAlertBridge/
RUN dotnet restore src/JabberAlertBridge --locked-mode
COPY src/JabberAlertBridge/ src/JabberAlertBridge/
RUN dotnet publish src/JabberAlertBridge -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app ./
USER $APP_UID
ENTRYPOINT ["dotnet", "JabberAlertBridge.dll"]
