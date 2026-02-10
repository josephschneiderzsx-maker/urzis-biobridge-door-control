# URZIS Door Monitoring (UDM)

Service Windows qui expose une API HTTP REST pour controler les portes a distance depuis n'importe quel appareil (telephone, tablette, web app, application mobile APK).

---

## Principe de fonctionnement

```
  Telephone / Web App / App Mobile (APK)
       |
       | HTTP POST/GET (WiFi ou reseau local)
       |
       v
  Serveur Windows (ex: 192.168.40.100:8080)
       |
       | UDM.exe (URZIS Door Monitoring)
       | API REST sur port 8080
       |
       v
  BioBridge SDK v3 (.NET)
       |
       | TCP/IP port 4370
       |
       v
  Terminal(s) BioBridge
  - 192.168.40.10 (porte 1)
  - 192.168.40.9  (porte 2)
  - ...
```

Le service tourne en permanence sur un PC Windows connecte au meme reseau que les terminaux BioBridge. Les appareils mobiles envoient des requetes HTTP pour ouvrir/fermer les portes.

---

## API REST - Reference complete

**URL de base** : `http://<IP_DU_SERVEUR>:8080`

> Remplacer `<IP_DU_SERVEUR>` par l'adresse IP du PC Windows ou le service tourne.
> En local : `http://localhost:8080`

---

### POST /open - Ouvrir une porte

Deverrouille une porte BioBridge pendant une duree donnee.

**Requete :**
```http
POST http://<IP_DU_SERVEUR>:8080/open
Content-Type: application/json

{
  "terminalIP": "192.168.40.10",
  "delay": 3000
}
```

**Parametres JSON :**

| Parametre | Type | Obligatoire | Defaut | Description |
|-----------|------|-------------|--------|-------------|
| `terminalIP` | string | Non | `192.168.40.10` | Adresse IP du terminal BioBridge |
| `delay` | integer | Non | `1000` | Duree du deverrouillage en millisecondes |

**Reponse succes (200) :**
```json
{
  "success": true,
  "message": "Door opened successfully",
  "delay": 3000,
  "status": "open"
}
```

**Reponse erreur (500) :**
```json
{
  "success": false,
  "message": "Failed to open door"
}
```

**Exemples :**

```bash
# Ouvrir la porte par defaut (192.168.40.10) pendant 1 seconde
curl -X POST http://192.168.40.100:8080/open

# Ouvrir la porte par defaut pendant 3 secondes
curl -X POST http://192.168.40.100:8080/open \
  -H "Content-Type: application/json" \
  -d '{"delay":3000}'

# Ouvrir une porte specifique (192.168.40.9) pendant 5 secondes
curl -X POST http://192.168.40.100:8080/open \
  -H "Content-Type: application/json" \
  -d '{"terminalIP":"192.168.40.9","delay":5000}'
```

> **Multi-terminal** : Le service gere automatiquement la deconnexion/reconnexion quand on change de terminal. Il suffit de changer `terminalIP` dans la requete.

---

### POST /close - Verifier la fermeture

Verifie l'etat de fermeture de la porte. La porte se referme automatiquement apres le delai de deverrouillage.

**Requete :**
```http
POST http://<IP_DU_SERVEUR>:8080/close
```

**Reponse :**
```json
{
  "success": true,
  "message": "Door will close automatically after unlock delay",
  "status": "closing"
}
```

---

### GET /status - Statut du service

Retourne l'etat actuel du service et le dernier evenement de porte.

**Requete :**
```http
GET http://<IP_DU_SERVEUR>:8080/status
```

**Reponse :**
```json
{
  "status": "Connected",
  "lastEvent": "Door opened (UnlockDoor called) at 2026-02-09 14:00:00",
  "eventsCount": 5,
  "connected": true
}
```

**Valeurs possibles de `status` :**

| Status | Signification |
|--------|---------------|
| `Connected` | Connecte au terminal, pret a fonctionner |
| `Open` | Porte actuellement deverrouillee |
| `Closed` | Porte fermee (evenement recu du terminal) |
| `Ajar` | Porte mal fermee (alarme) |
| `Disconnected` | Deconnecte du terminal |
| `SDK Not Registered` | SDK BioBridge non installe/enregistre |
| `Initialization Failed` | Erreur d'initialisation du SDK |

---

## Evenements de porte (OnDoor)

Le service ecoute les evenements envoyes par le terminal BioBridge en temps reel :

| Type | Description |
|------|-------------|
| 1 | La porte est ouverte soudainement |
| 4 | La porte n'est pas bien fermee (alarme) |
| 5 | La porte est fermee |
| 53 | La porte est ouverte via le bouton Off-Exit |

Ces evenements sont enregistres dans l'historique (visible via `/status`).

---

## Integration avec une application mobile / web

### Option 1 : Web App (HTML/JavaScript) - Le plus simple

Creer un fichier HTML accessible depuis le telephone :

```html
<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>UDM - URZIS Door Monitoring</title>
  <style>
    body { font-family: Arial; text-align: center; padding: 20px; background: #1a1a2e; color: white; }
    .btn { display: block; width: 80%; max-width: 300px; margin: 15px auto; padding: 20px;
           border: none; border-radius: 12px; font-size: 18px; font-weight: bold;
           cursor: pointer; color: white; }
    .btn-open { background: #4CAF50; }
    .btn-open:active { background: #388E3C; }
    .btn-status { background: #2196F3; }
    .btn-close { background: #f44336; }
    #result { margin-top: 20px; padding: 15px; border-radius: 8px; background: #16213e; min-height: 50px; }
    h1 { font-size: 24px; }
    select, input { padding: 10px; font-size: 16px; border-radius: 8px; border: 1px solid #555;
                    background: #16213e; color: white; width: 80%; max-width: 300px; margin: 5px; }
  </style>
</head>
<body>
  <h1>UDM - URZIS Door Monitoring</h1>

  <label>Terminal :</label><br>
  <select id="terminal">
    <option value="192.168.40.10">Porte 1 (192.168.40.10)</option>
    <option value="192.168.40.9">Porte 2 (192.168.40.9)</option>
  </select><br><br>

  <label>Duree (ms) :</label><br>
  <input type="number" id="delay" value="3000" min="500" max="30000"><br><br>

  <button class="btn btn-open" onclick="openDoor()">OUVRIR LA PORTE</button>
  <button class="btn btn-status" onclick="getStatus()">STATUT</button>
  <button class="btn btn-close" onclick="closeDoor()">FERMER</button>

  <div id="result">En attente...</div>

  <script>
    // IMPORTANT: Remplacer par l'IP du serveur Windows
    const SERVER = "http://192.168.40.100:8080";

    async function openDoor() {
      const terminal = document.getElementById("terminal").value;
      const delay = document.getElementById("delay").value;
      showResult("Ouverture en cours...");
      try {
        const res = await fetch(SERVER + "/open", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ terminalIP: terminal, delay: parseInt(delay) })
        });
        const data = await res.json();
        showResult(data.success ? "PORTE OUVERTE (" + data.delay + "ms)" : "ERREUR: " + data.message);
      } catch (e) {
        showResult("Erreur de connexion: " + e.message);
      }
    }

    async function closeDoor() {
      try {
        const res = await fetch(SERVER + "/close", { method: "POST" });
        const data = await res.json();
        showResult(data.message);
      } catch (e) {
        showResult("Erreur: " + e.message);
      }
    }

    async function getStatus() {
      try {
        const res = await fetch(SERVER + "/status");
        const data = await res.json();
        showResult("Status: " + data.status + "\nDernier: " + data.lastEvent + "\nEvents: " + data.eventsCount);
      } catch (e) {
        showResult("Erreur: " + e.message);
      }
    }

    function showResult(msg) {
      document.getElementById("result").innerText = msg;
    }
  </script>
</body>
</html>
```

**Utilisation :** Ouvrir ce fichier dans le navigateur du telephone (Chrome, Safari). Le telephone doit etre connecte au meme reseau WiFi que le serveur.

---

### Option 2 : Application Android (APK) avec WebView

Creer une app Android minimale qui encapsule la web app :

**MainActivity.java :**
```java
package com.urzis.doormonitoring;

import android.os.Bundle;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import androidx.appcompat.app.AppCompatActivity;

public class MainActivity extends AppCompatActivity {
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        WebView webView = new WebView(this);
        setContentView(webView);

        WebSettings settings = webView.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        webView.setWebViewClient(new WebViewClient());

        // Charger la page web hebergee sur le serveur ou embarquee dans l'APK
        webView.loadUrl("http://192.168.40.100:8080/index.html");
        // OU charger depuis les assets locaux :
        // webView.loadUrl("file:///android_asset/index.html");
    }
}
```

> **Alternative plus simple** : Utiliser Apache Cordova ou un outil comme [WebIntoApp](https://webintoapp.com) pour convertir la page HTML en APK sans coder.

---

### Option 3 : Appels HTTP directs (n'importe quel langage)

Depuis n'importe quel langage ou plateforme :

**JavaScript (fetch) :**
```javascript
fetch("http://192.168.40.100:8080/open", {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ terminalIP: "192.168.40.10", delay: 3000 })
})
.then(res => res.json())
.then(data => console.log(data));
```

**Python :**
```python
import requests
r = requests.post("http://192.168.40.100:8080/open",
    json={"terminalIP": "192.168.40.10", "delay": 3000})
print(r.json())
```

**cURL (ligne de commande) :**
```bash
curl -X POST http://192.168.40.100:8080/open \
  -H "Content-Type: application/json" \
  -d '{"terminalIP":"192.168.40.10","delay":3000}'
```

**PowerShell :**
```powershell
Invoke-WebRequest -UseBasicParsing -Method POST -Uri http://localhost:8080/open `
  -Body '{"terminalIP":"192.168.40.10","delay":3000}' -ContentType "application/json"
```

**C# / .NET :**
```csharp
using var client = new HttpClient();
var content = new StringContent(
    "{\"terminalIP\":\"192.168.40.10\",\"delay\":3000}",
    Encoding.UTF8, "application/json");
var response = await client.PostAsync("http://192.168.40.100:8080/open", content);
var result = await response.Content.ReadAsStringAsync();
```

**Kotlin (Android) :**
```kotlin
val url = URL("http://192.168.40.100:8080/open")
val conn = url.openConnection() as HttpURLConnection
conn.requestMethod = "POST"
conn.setRequestProperty("Content-Type", "application/json")
conn.doOutput = true
conn.outputStream.write("""{"terminalIP":"192.168.40.10","delay":3000}""".toByteArray())
val response = conn.inputStream.bufferedReader().readText()
```

---

## Installation complete

### Prerequis

- Windows 10/11 ou Windows Server
- .NET Framework 4.8
- BioBridge SDK v3.0.007 installe (`BioBridgeSDKDLLv3.dll` dans `C:\Windows\SysWOW64\`)
- `zkemkeeper.dll` enregistre comme COM
- Acces reseau TCP aux terminaux BioBridge (port 4370)

### Etape 1 : Enregistrer zkemkeeper.dll

En tant qu'Administrateur :
```cmd
regsvr32 C:\Windows\SysWOW64\zkemkeeper.dll
```

### Etape 2 : Generer Interop.zkemkeeper.dll (si absent)

Executer dans **PowerShell 32-bit** (`C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe`) :

```powershell
$signature = @"
[DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
public static extern void LoadTypeLibEx(string szFile, int regKind, [MarshalAs(UnmanagedType.Interface)] out object typeLib);
"@
Add-Type -MemberDefinition $signature -Name "OleAut32" -Namespace "Win32"

Add-Type -TypeDefinition @"
using System;
using System.Reflection;
using System.Runtime.InteropServices;
public class TlbCb : ITypeLibImporterNotifySink {
    public void ReportEvent(ImporterEventKind k, int c, string m) { }
    public Assembly ResolveRef(object t) { return null; }
}
"@

$typeLib = $null
[Win32.OleAut32]::LoadTypeLibEx("C:\Windows\SysWOW64\zkemkeeper.dll", 0, [ref]$typeLib)
$converter = New-Object System.Runtime.InteropServices.TypeLibConverter
$asm = $converter.ConvertTypeLibToAssembly($typeLib, "Interop.zkemkeeper.dll", 0, (New-Object TlbCb), $null, $null, "zkemkeeper", $null)
$asm.Save("Interop.zkemkeeper.dll")
```

Copier le fichier genere dans le dossier du projet (`BioBridgeDoorControlService\`).

### Etape 3 : Compiler le projet

```cmd
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" BioBridgeDoorControlService\BioBridgeDoorControlService.vbproj -p:Configuration=Debug -p:Platform=AnyCPU -t:Build
```

Le fichier compile s'appelle `UDM.exe`.

### Etape 4 : Deployer les fichiers

Creer le dossier et copier les fichiers :

```cmd
mkdir C:\UDM
copy BioBridgeDoorControlService\bin\Debug\UDM.exe C:\UDM\
copy BioBridgeDoorControlService\bin\Debug\BioBridgeSDKDLLv3.dll C:\UDM\
copy BioBridgeDoorControlService\bin\Debug\Interop.zkemkeeper.dll C:\UDM\
```

**Fichiers dans `C:\UDM\` :**

| Fichier | Description |
|---------|-------------|
| `UDM.exe` | Executable du service URZIS Door Monitoring |
| `BioBridgeSDKDLLv3.dll` | SDK BioBridge (.NET assembly) |
| `Interop.zkemkeeper.dll` | COM Interop (genere a l'etape 2) |

### Etape 5 : Installer le service Windows

En tant qu'Administrateur :
```cmd
sc create UDM binpath= "C:\UDM\UDM.exe" start= auto displayname= "URZIS Door Monitoring (UDM)"
```

### Etape 6 : Demarrer le service

```cmd
net start UDM
```

### Etape 7 : Verifier

```powershell
# Verifier que le service tourne
Get-Service UDM

# Tester l'API
Invoke-WebRequest -UseBasicParsing -Uri http://localhost:8080/status
```

---

## Configuration reseau pour acces a distance

Pour que les telephones et applications puissent acceder au service :

### 1. Autoriser le port 8080 dans le pare-feu Windows

En tant qu'Administrateur :
```cmd
netsh advfirewall firewall add rule name="UDM - URZIS Door Monitoring" dir=in action=allow protocol=tcp localport=8080
```

### 2. Configurer le HttpListener pour accepter les connexions distantes

Par defaut, le service ecoute uniquement sur `localhost`. Pour accepter les connexions depuis d'autres appareils du reseau, modifier le prefix HttpListener dans `Service1.vb` :

De :
```
http://localhost:8080/
```

Vers (necessite droits admin pour le service) :
```
http://+:8080/
```

Puis autoriser l'URL avec la commande (Administrateur) :
```cmd
netsh http add urlacl url=http://+:8080/ user=Everyone
```

### 3. Trouver l'IP du serveur Windows

```cmd
ipconfig
```

Utiliser l'adresse IPv4 du serveur (ex: `192.168.40.100`) dans les applications clientes.

### 4. Verifier la connectivite depuis le telephone

Ouvrir le navigateur du telephone et aller sur :
```
http://192.168.40.100:8080/status
```

Si la reponse JSON s'affiche, la connexion fonctionne.

---

## Configuration

Constantes dans `Service1.vb` :

| Constante | Defaut | Description |
|-----------|--------|-------------|
| `HTTP_PORT` | `8080` | Port du serveur HTTP REST |
| `DEFAULT_TERMINAL_IP` | `192.168.40.10` | IP du terminal BioBridge par defaut |
| `DEFAULT_TERMINAL_PORT` | `4370` | Port TCP du terminal BioBridge |

---

## Architecture technique

```
Telephone / Web App / App Mobile
    |
    | HTTP POST/GET (port 8080, reseau WiFi)
    v
UDM.exe - URZIS Door Monitoring (.NET 4.8, x86)
    |-- HttpListener (serveur REST API)
    |     |-- POST /open   -> OpenDoor(terminalIP, delay)
    |     |-- POST /close  -> CheckClose()
    |     |-- GET  /status -> GetStatus()
    |
    |-- BioBridgeSDKDLLv3.dll (.NET assembly)
    |     |-- Connect_TCPIP(ip, port)
    |     |-- Disconnect()
    |     |-- UnlockDoor(delay)
    |     |-- Events: OnDoor, OnConnected, OnDisConnected
    |     |
    |     |-- Interop.zkemkeeper.dll (COM Interop genere)
    |           |-- zkemkeeper.dll (COM, enregistre dans SysWOW64)
    |
    | TCP/IP (port 4370)
    v
Terminal(s) BioBridge
    |-- 192.168.40.10 (porte 1)
    |-- 192.168.40.9  (porte 2)
    |-- ...
```

**Gestion multi-terminal** : Le SDK ne supporte qu'une connexion a la fois. Quand on envoie un `terminalIP` different, le service fait automatiquement `Disconnect()` puis `Connect_TCPIP()` vers le nouveau terminal.

---

## Fichiers du projet

```
BioBridgeDoorControl\
  README.md                          -- Cette documentation
  SDK-ISSUE.md                       -- Historique de resolution du SDK
  BioBridgeDoorControlService\
    Service1.vb                      -- Code principal du service UDM
    ProjectInstaller.vb              -- Installeur Windows Service
    AssemblyInfo.vb                  -- Informations d'assembly
    BioBridgeDoorControlService.vbproj -- Fichier projet MSBuild
    Interop.zkemkeeper.dll           -- COM Interop (genere)
    bin\Debug\
      UDM.exe                         -- Executable compile
      BioBridgeSDKDLLv3.dll            -- SDK BioBridge
      Interop.zkemkeeper.dll           -- COM Interop
```

---

## Logs et surveillance

Tous les evenements sont enregistres dans **Event Viewer Windows** :

- **Source** : `UDM`
- **Log** : `Application`
- **Emplacement** : Event Viewer > Windows Logs > Application

**Consulter les derniers logs :**
```powershell
Get-EventLog -LogName Application -Source UDM -Newest 20 | Format-List TimeGenerated,Message
```

**Evenements enregistres :**
- Demarrage/arret du service
- Connexion/deconnexion aux terminaux
- Ouverture de porte (IP, duree, succes/echec)
- Evenements de porte (ouverture, fermeture, alarme)
- Erreurs et exceptions

---

## Depannage

### Le service ne demarre pas
```powershell
# Verifier les logs
Get-EventLog -LogName Application -Source UDM -Newest 5 | Format-List TimeGenerated,Message

# Verifier que le port n'est pas utilise
netstat -an | findstr 8080

# Verifier que les DLL sont presentes
dir C:\UDM\
```

### Erreur "Unable to connect to the remote server" depuis le telephone
- Verifier que le pare-feu autorise le port 8080
- Verifier que le HttpListener ecoute sur `http://+:8080/` (pas seulement `localhost`)
- Verifier que le telephone est sur le meme reseau WiFi
- Tester : `http://<IP_SERVEUR>:8080/status` dans le navigateur du telephone

### Erreur "Interop.zkemkeeper not found"
- Regenerer `Interop.zkemkeeper.dll` (voir Etape 2 de l'installation)
- Verifier que le fichier est dans le meme dossier que l'exe

### Erreur "Failed to connect to terminal"
```cmd
# Verifier la connectivite
ping 192.168.40.10

# Verifier le port
powershell Test-NetConnection 192.168.40.10 -Port 4370
```

### Status "SDK Not Registered"
```cmd
# Re-enregistrer les DLL COM
regsvr32 C:\Windows\SysWOW64\zkemkeeper.dll
```

### Smart App Control bloque l'exe
- Desactiver Smart App Control : Parametres > Securite Windows > Controle des applications
- Ou signer l'executable avec un certificat de confiance

### Le service crash quand on change de terminal
- Le SDK a un delai de stabilisation de 500ms entre deconnexion et reconnexion
- Si le probleme persiste, redemarrer le service : `net stop UDM && net start UDM`

---

## Securite

**Important** : Ce service n'a pas d'authentification. Toute personne sur le reseau local peut ouvrir les portes.

**Recommandations pour la production :**
- Restreindre l'acces au port 8080 par IP dans le pare-feu Windows
- Utiliser un VPN pour l'acces depuis l'exterieur du reseau local
- Ajouter un mecanisme d'authentification (token, API key) dans une future version
- Ne pas exposer le port 8080 sur Internet directement

---

## Gestion du service

```cmd
# Demarrer
net start UDM

# Arreter
net stop UDM

# Redemarrer
net stop UDM && net start UDM

# Supprimer le service (desinstallation)
net stop UDM
sc delete UDM

# Voir le statut
sc query UDM
```

---

## Migration depuis l'ancien service (BioBridgeDoorControl)

Si l'ancien service est installe, le supprimer d'abord :
```cmd
net stop BioBridgeDoorControl
sc delete BioBridgeDoorControl
```

Puis suivre les etapes d'installation ci-dessus.
