# Dockerfile: receta para que Railway/Render construyan y ejecuten la app en un "contenedor"
# Se hace en 2 etapas: una con el SDK para compilar y otra liviana solo para ejecutar

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# ↑ Etapa 1: imagen oficial de Microsoft con el SDK de .NET 10 (trae el compilador)
WORKDIR /src
# ↑ Carpeta de trabajo dentro del contenedor
COPY GestionPedidos.csproj ./
# ↑ Copia primero solo el archivo de proyecto (así Docker reutiliza la descarga de paquetes si no cambió)
RUN dotnet restore
# ↑ Descarga los paquetes NuGet (Npgsql / Entity Framework)
COPY . ./
# ↑ Copia el resto del código fuente
RUN dotnet publish -c Release -o /app --no-restore
# ↑ Compila en modo Release (optimizado) y deja el resultado en /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# ↑ Etapa 2: imagen liviana que solo trae lo necesario para EJECUTAR apps ASP.NET Core
WORKDIR /app
# ↑ Carpeta de trabajo
COPY --from=build /app ./
# ↑ Trae los archivos compilados de la etapa 1
ENV ASPNETCORE_ENVIRONMENT=Production
# ↑ Indica que estamos en producción (activa la página de error amigable)
EXPOSE 8080
# ↑ Documenta el puerto por defecto (Railway igual informa el suyo en la variable PORT)
ENTRYPOINT ["dotnet", "GestionPedidos.dll"]
# ↑ Comando que arranca la aplicación
