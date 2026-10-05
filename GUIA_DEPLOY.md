# Guía para publicar la app (sin programar)

Tiempo estimado: 30 a 60 minutos. Solo clics en el navegador.

Vas a hacer 3 cosas:
1. Subir el código a **GitHub** (el "repositorio" que pide el challenge).
2. Publicarlo en **Railway** con una base **PostgreSQL**.
3. Probar que todo ande y completar las URLs en el README.

---

## Parte 1 — Subir el código a GitHub

1. Entrá a <https://github.com> y creá una cuenta (o iniciá sesión).
2. Arriba a la derecha tocá **+** → **New repository**.
3. Completá:
   - **Repository name:** `gestion-pedidos`
   - Marcá **Public** (así quien revisa puede verlo).
   - **No** marques "Add a README" (ya tenemos uno).
4. Tocá **Create repository**.
5. En la página que aparece, hacé clic en el enlace **uploading an existing file**.
6. Descomprimí el `.zip` en tu computadora. Abrí la carpeta `GestionPedidos` y **arrastrá todo su contenido** (las carpetas `Controllers`, `Data`, `Models`, `Services`, `Views` y los archivos sueltos) a la página de GitHub.
   - Importante: arrastrá lo que está **adentro** de la carpeta, no la carpeta en sí. El `Dockerfile` tiene que quedar en la raíz del repositorio.
   - Si los archivos que empiezan con punto (`.gitignore`, `.dockerignore`) no se suben, no pasa nada: son opcionales.
7. Abajo, en "Commit changes", escribí `Versión inicial` y tocá **Commit changes**.
8. Verificá que en la página del repositorio se vean `Dockerfile`, `Program.cs` y las carpetas.

---

## Parte 2 — Publicar en Railway

1. Entrá a <https://railway.com> e iniciá sesión **con tu cuenta de GitHub** (botón "Login with GitHub").
   - Railway ofrece un crédito de prueba; revisá en su página los precios vigentes antes de empezar.
2. Tocá **New Project** → **Deploy from GitHub repo** → elegí `gestion-pedidos`.
   - Si no aparece, tocá **Configure GitHub App** y dale acceso a ese repositorio.
3. Railway detecta el `Dockerfile` solo y empieza a construir. **El primer intento va a fallar** porque todavía no hay base de datos: es normal.
4. Agregá la base: en el lienzo del proyecto tocá **+ New** (o **Create**) → **Database** → **PostgreSQL**. Esperá a que aparezca el recuadro "Postgres".
5. Conectá la app con la base:
   - Hacé clic en el recuadro de tu app (`gestion-pedidos`).
   - Pestaña **Variables** → **New Variable** → **Add Reference** (o "Add Reference Variable").
   - Elegí el servicio **Postgres** y la variable **DATABASE_URL**. Guardá.
   - Si te pide escribirla a mano: nombre `DATABASE_URL`, valor `${{Postgres.DATABASE_URL}}`.
6. Railway vuelve a publicar solo. Si no, tocá **Deploy** / **Redeploy**.
7. Generá la dirección pública:
   - En tu app: pestaña **Settings** → sección **Networking** → **Generate Domain**.
   - Si te pregunta el puerto, poné **8080**.
   - Te va a dar algo como `https://gestion-pedidos-production.up.railway.app`.
8. (Opcional) Clave del webhook: en **Variables** agregá `Webhook__Secret` con una clave (ej. `mi-clave-123`). Si la ponés, avisá la clave a quien revisa. Si no, el webhook queda abierto (más simple para la prueba).

### Si algo falla
- Pestaña **Deployments** → último deploy → **View logs**. Leé las últimas líneas.
- "La base todavía no responde": esperá 1 minuto; la app reintenta 10 veces.
- "Falta configurar la base": no se cargó `DATABASE_URL` (volvé al paso 5).
- Copiá el error y pegámelo en el chat; lo resolvemos.

---

## Parte 3 — Probar

1. Abrí tu URL: tenés que ver la lista de 5 clientes de ejemplo.
2. Entrá a **Pedidos** → pedido **#15**: tiene que mostrar el recuadro "Seguimiento (API externa)" con código, transportista y fecha.
3. Probá el webhook desde la página <https://reqbin.com> (o Postman):
   - Método **POST**
   - URL: `https://TU-APP.up.railway.app/api/webhooks/orders`
   - Body (JSON):
     ```json
     {"event":"order.status.changed","orderId":15,"status":"DELIVERED"}
     ```
   - Tiene que responder `{"ok":true,"mensaje":"Pedido 15: Pendiente → Entregado"}`.
4. Recargá el pedido #15: ahora dice **Entregado**. Y en **Webhooks recibidos** aparece el evento.

---

## Parte 4 — Completar el README y entregar

1. En GitHub abrí `README.md` → ícono del lápiz ✏️.
2. Reemplazá `TU-APP.up.railway.app` por tu URL real y `TU-USUARIO` por tu usuario de GitHub. **Commit changes**.
3. Mandá a la empresa:
   - URL de la app
   - URL del webhook: `https://TU-APP.up.railway.app/api/webhooks/orders`
   - Ejemplo de JSON del webhook y la clave si configuraste una
   - URL del repositorio

**Importante:** la app tiene que seguir publicada el día de la revisión. No borres el proyecto de Railway hasta que termine el proceso.
