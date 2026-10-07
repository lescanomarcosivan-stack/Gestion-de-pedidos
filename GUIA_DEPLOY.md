# Guía para publicar la versión 2 (sin programar)

Tu app ya está publicada en Railway. Ahora hay que **subir los archivos nuevos a GitHub** y **cargar algunas variables** en Railway. Railway vuelve a publicar solo.

Tiempo estimado: 20 minutos para lo obligatorio (Partes 1 a 4). Google y email son opcionales (Partes 5 y 6).

> **Ojo:** al arrancar la versión nueva, la app detecta que la base es de la versión anterior y **recrea las tablas con datos de ejemplo**. Se pierden los clientes y pedidos que hayas cargado a mano (son datos de prueba, así que no importa).

---

## Parte 1 — Subir los archivos nuevos a GitHub

1. Descomprimí el `.zip` nuevo en tu computadora (clic derecho → **Extraer todo**). Entrá a la carpeta hasta ver `Controllers`, `Models`, `Views`, `Program.cs`, `Dockerfile`, etc.
2. En el navegador, abrí tu repositorio en **github.com** (el que se llama `Gestion-de-pedidos`).
3. Arriba a la derecha de la lista de archivos tocá **Add file** → **Upload files**.
4. Seleccioná **todo** el contenido de la carpeta (**Ctrl + A**) y **arrastralo** a la página.
   - GitHub reemplaza los archivos que ya existían y agrega los nuevos. No hace falta borrar nada antes.
5. Esperá que termine de cargar la lista (son unos 55 archivos).
6. Abajo, en el cuadro de texto, escribí: `Versión 2: login, roles, webhook firmado, dashboard`
7. Tocá **Commit changes**.
8. **Para comprobar:** entrá a la carpeta `Controllers`. Tiene que aparecer `CuentaController.cs`. Si aparece, se subió bien.

Railway detecta el cambio y empieza a publicar solo. **Todavía no lo abras**: primero cargá las variables (Parte 2).

---

## Parte 2 — Cargar las variables en Railway

1. Entrá a **railway.com** → tu proyecto → clic en el recuadro **Gestion-de-pedidos**.
2. Pestaña **Variables** → **+ New Variable**. Cargá estas, una por una (nombre exacto, con **dos** guiones bajos):

| Nombre | Valor |
|---|---|
| `Webhook__Secret` | Un texto largo e inventado, de al menos 32 caracteres (letras y números, sin espacios). **No lo escribas en ningún archivo del repositorio**: el repositorio es público |
| `Demo__Password` | Una contraseña tuya para los usuarios demo (mínimo 8 caracteres, con letras y números) |
| `App__UrlPublica` | `https://gestion-de-pedidos-production.up.railway.app` |

3. Si aparece el botón **Deploy** o **Apply changes**, tocalo.
4. Pestaña **Deployments**: esperá a que el último diga **Active / Success** (3 a 5 minutos).
   - Si sale **Failed** o **Crashed**: tocá **View logs** y mandame una captura de las últimas líneas.

> Guardá la clave del webhook en un lugar seguro: se la vas a pasar a quien revise el challenge.

---

## Parte 3 — Primer ingreso

1. Abrí `https://gestion-de-pedidos-production.up.railway.app`. Ahora aparece la **pantalla de login**.
2. Entrá con:
   - Email: `admin@demo.com`
   - Contraseña: la que pusiste en `Demo__Password`
3. Vas a ver el **Dashboard** con los pedidos por estado.

---

## Parte 4 — Probar lo nuevo (checklist)

- [ ] **Pedidos** → poné Desde = hoy y Hasta = ayer → **Filtrar**: aparece un error en rojo.
- [ ] Buscá `zzzz` → dice "Ningún pedido coincide…" → tocá **✕ Limpiar filtros**.
- [ ] Abajo de la tabla está la **paginación** (1, 2, 3).
- [ ] Los montos se ven como `$ 1.500,50`.
- [ ] Abrí un pedido **Cancelado** (filtrá por estado Cancelado): no muestra seguimiento.
- [ ] Cambiá el estado de un pedido y mirá el **Historial de estados** abajo.
- [ ] **Webhooks** → **Probar webhook** → **Enviar evento**: dice "✓ Evento procesado correctamente".
- [ ] Probá de nuevo con "Firma con clave equivocada": dice "✗ Evento rechazado" (401).
- [ ] **Salir** → entrá con `consulta@demo.com`: no aparecen botones para crear ni editar.
- [ ] **Salir** → **Crear cuenta** con un email tuyo → te dice que espera aprobación. Entrá como admin → **Usuarios** → **Activar**.
- [ ] **Registro**: aparecen todos los logins, cambios y webhooks que hiciste.

---

## Parte 5 (opcional) — Ingresar con Google

Necesitás una cuenta de Google. Son unos 15 minutos.

1. Entrá a **console.cloud.google.com** e iniciá sesión.
2. Arriba, en el selector de proyectos → **Proyecto nuevo** → nombre `Gestion Pedidos` → **Crear**. Asegurate de que quede seleccionado.
3. En el buscador de arriba escribí **"Google Auth Platform"** (o "Pantalla de consentimiento de OAuth") y entrá.
4. Tocá **Comenzar** y completá:
   - Nombre de la app: `Gestión de Pedidos`
   - Correo de asistencia: tu email
   - Público: **Externo**
   - Información de contacto: tu email → aceptá y **Crear**.
5. En el menú de la izquierda: **Público** → **Publicar app** → confirmar. (Si queda "En prueba", solo podrán entrar los emails que agregues como usuarios de prueba.)
6. Menú de la izquierda: **Clientes** → **Crear cliente**:
   - Tipo de aplicación: **Aplicación web**
   - Nombre: `Railway`
   - **URIs de redireccionamiento autorizados** → **Agregar URI** → pegá exactamente:
     `https://gestion-de-pedidos-production.up.railway.app/signin-google`
   - **Crear**.
7. Aparece una ventana con el **ID de cliente** y el **Secreto del cliente**. Copialos (botón de copiar).
8. En Railway → **Variables**, agregá:

| Nombre | Valor |
|---|---|
| `Autenticacion__Google__ClientId` | el ID de cliente |
| `Autenticacion__Google__ClientSecret` | el secreto del cliente |

9. Esperá el nuevo deploy. En el login aparece **Ingresar con Google**.
10. La primera vez que alguien entra con Google, su cuenta queda **pendiente**: un admin la activa en **Usuarios**.

Si Google muestra el error `redirect_uri_mismatch`, revisá que la URI del paso 6 sea idéntica (https, sin barra al final).

---

## Parte 6 (opcional) — Enviar emails de recuperación de contraseña

Sin esto, la recuperación funciona igual, pero el enlace **no llega por email**: queda escrito en los logs de Railway (Deployments → View logs → buscá "Email NO enviado").

1. Creá una cuenta gratis en **brevo.com**.
2. Menú de tu cuenta → **Senders, Domains & Dedicated IPs** (Remitentes) → **Add sender** → poné tu Gmail → Brevo te manda un código → verificalo.
3. Menú de tu cuenta → **SMTP & API** → pestaña **API Keys** → **Generate a new API key** → nombre `railway` → copiala (se muestra una sola vez).
4. En Railway → **Variables**, agregá:

| Nombre | Valor |
|---|---|
| `Email__BrevoApiKey` | la clave que copiaste |
| `Email__Remitente` | tu Gmail verificado en el paso 2 |

5. Probá: **Salir** → **¿Olvidaste tu contraseña?** → poné un email de una cuenta activa → revisá la bandeja (y **spam**).

---

## Qué mandarle a la empresa

- URL de la app y los 3 usuarios demo con la contraseña.
- URL del webhook: `https://gestion-de-pedidos-production.up.railway.app/api/webhooks/orders`
- La clave del webhook (`Webhook__Secret`) y cómo firmar (está en el README).
- URL del repositorio de GitHub.

---

# Versión 3: productos, stock, emails y reportes

**Esta actualización NO borra datos.** La app agrega sola las tablas y columnas nuevas, y carga 8 productos de ejemplo si no hay ninguno. Usuarios, clientes, pedidos e historial se conservan.

## Subir la versión 3
1. Descomprimí el zip nuevo y entrá a la carpeta hasta ver `Controllers`, `Views`, `Program.cs`, etc.
2. En GitHub: **Add file → Upload files**, arrastrá todo el contenido y tocá **Commit changes**.
3. En Railway (proyecto **genuine-friendship**), esperá a que el deploy diga **Active**.

## Variable nueva (opcional)
| Nombre | Valor |
|---|---|
| `Notificaciones__EmailsInternos` | Los emails del equipo que reciben avisos (pedido nuevo, cancelado, stock bajo), separados por coma. Ej.: `tuemail@gmail.com` |

Los emails solo salen de verdad si configuraste Brevo (Parte 6). Si no, igual quedan registrados en **Registro → tipo Email** como "Email NO enviado".

## Probar lo nuevo (checklist)
- [ ] Menú **Productos**: aparecen 8 productos y un aviso rojo de stock bajo.
- [ ] Abrí un producto → **Mover stock** → Ingreso de 10 → el stock sube y aparece en el historial.
- [ ] **Pedidos → + Nuevo pedido**: agregá dos productos, poné un descuento en un renglón y un descuento general. El total se calcula en vivo.
- [ ] Pedí más cantidad que el stock: el número se pone en rojo y al guardar avisa "Stock insuficiente".
- [ ] Guardá el pedido: el stock de esos productos baja.
- [ ] Cancelá ese pedido: el stock vuelve. Reabrilo (estado Pendiente): se descuenta de nuevo.
- [ ] **Pedidos → ⬇ Excel / ⬇ PDF**: se descargan con los filtros que tengas puestos.
- [ ] **Productos → ⬇ Excel / ⬇ PDF**: reporte de stock.
- [ ] Editá un cliente y marcá **Recibe notificaciones por email**. Los clientes de ejemplo vienen sin marcar, a propósito.
- [ ] **Registro → tipo Email**: se ven los avisos generados.

---

# Versión 4 (etapa 2): Google Drive y Google Calendar

**No borra datos.** Agrega la fecha de entrega a los pedidos, los archivos adjuntos y la conexión con Google.

**No hace falta ninguna variable nueva en Railway**: usa las mismas credenciales de Google del login (`Autenticacion__Google__ClientId` y `__ClientSecret`).

Cómo funciona: un administrador conecta **una vez** la cuenta de Google de la empresa. Desde ahí, la app guarda los adjuntos en el Drive de esa cuenta y agenda las entregas en su Calendar. Los demás usuarios no necesitan cuenta de Google.

> Usá una cuenta de Google **de la empresa** (por ejemplo, un Gmail creado para esto), no tu cuenta personal: los archivos y eventos quedan ahí.

## Parte A — Preparar Google Cloud (10 minutos)

Entrá a **console.cloud.google.com** y verificá que arriba esté seleccionado el proyecto **Gestion Pedidos** (el mismo del login con Google).

**1. Habilitar las dos APIs**
1. En el buscador de arriba escribí **Google Drive API** → entrá → botón **Habilitar**.
2. Volvé al buscador, escribí **Google Calendar API** → entrá → **Habilitar**.

**2. Agregar la dirección de retorno**
1. Buscador → **Google Auth Platform** → menú izquierdo **Clientes** → tocá el cliente **Railway**.
2. En **URIs de redireccionamiento autorizados** → **Agregar URI** → pegá exactamente:
   `https://gestion-de-pedidos-production.up.railway.app/Integraciones/Callback`
3. Dejá la que ya estaba (`/signin-google`). Tocá **Guardar**.

**3. Agregar los permisos a la pantalla de consentimiento**
1. Menú izquierdo **Acceso a los datos** → **Agregar o quitar permisos**.
2. En el filtro escribí `drive.file` y marcá **.../auth/drive.file**.
3. Escribí `calendar.events` y marcá **.../auth/calendar.events**.
4. **Actualizar** → abajo **Guardar**.

**4. Verificar que la app esté publicada**
Menú izquierdo **Público** → tiene que decir **En producción**. Si dice **Prueba**, la conexión se corta sola a los 7 días.

## Parte B — Subir el código
1. Descomprimí el zip nuevo.
2. En GitHub: **Add file → Upload files** → arrastrá todo el contenido → **Commit changes**.
3. **Comprobá** que en GitHub existan la carpeta `Views/Integraciones` (con `Index.cshtml`) y el archivo `Controllers/IntegracionesController.cs`. Si falta la carpeta, subila de nuevo entrando a `Views`.
4. En Railway (**genuine-friendship**), esperá a que el deploy diga **Active**.

## Parte C — Conectar la cuenta (una sola vez)
1. Entrá a la app como **admin** → menú **Google**.
2. Abajo a la derecha se ve la **Dirección de retorno**: tiene que ser idéntica a la que cargaste en el paso A.2.
3. Tocá **Conectar cuenta de Google** → elegí la cuenta de la empresa.
4. Google va a mostrar **"Google no verificó esta app"**. Es normal (la verificación es un trámite para apps públicas). Tocá **Configuración avanzada** → **Ir a Gestión de Pedidos (no seguro)**.
5. **Marcá las dos casillas** (Drive y Calendar) → **Continuar**.
6. Volvés a la app: "Cuenta ... conectada". Tocá **Probar conexión**: tiene que decir ✓ Drive ... · Calendar ...

Si sale `redirect_uri_mismatch`: la dirección del paso A.2 no es idéntica (https, sin barra al final).
Si sale un error que menciona que la API "has not been used" o está "disabled": falta el paso A.1.

## Probar (checklist)
- [ ] **Pedidos → + Nuevo pedido**: elegí una **Fecha de entrega** → guardá. En el detalle, a los segundos aparece "📅 en Google Calendar". Abrí el Calendar de la empresa: está el evento.
- [ ] En el detalle: **Cambiar fecha** → el evento se mueve de día.
- [ ] Cambiá el estado a **Entregado** → el evento se pone verde con ✓.
- [ ] Cancelá un pedido con fecha → el evento desaparece de Calendar.
- [ ] **Archivos** → elegí un PDF → **Subir a Drive**. En el Drive de la empresa aparece la carpeta **Gestión de Pedidos** con el archivo.
- [ ] Tocá el nombre del archivo → se descarga.
- [ ] **Quitar** (dos clics) → el archivo va a la papelera de Drive.
- [ ] Entrá con `consulta@demo.com`: puede descargar, pero no subir ni quitar.
- [ ] **Registro → tipo Google**: aparecen la conexión, los archivos y los eventos.
- [ ] Menú **Google → Sincronizar entregas**: agenda en Calendar todos los pedidos que ya tenían fecha.
