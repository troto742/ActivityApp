# Home Manager

Primera entrega funcional de una app doméstica colaborativa.

## Arranque

1. `docker compose up -d`
2. `dotnet tool restore` y `dotnet tool run dotnet-ef database update --project src/backend/HomeManager.Api`
3. `dotnet run --project src/backend/HomeManager.Api --urls http://0.0.0.0:5080`
4. En otra terminal: `cd apps/mobile` y `npm start`

Android Emulator usa por defecto `http://10.0.2.2:5080`. En un teléfono físico define `EXPO_PUBLIC_API_URL` con la IP LAN del equipo. Swagger queda en `http://localhost:5080/swagger`.

Las claves de `appsettings.json` son únicamente de desarrollo. En despliegue deben venir de secretos y rotarse.

Consulta [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) para el diseño, contratos, esquema objetivo y alcance pendiente.
