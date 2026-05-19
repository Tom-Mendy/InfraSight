# Client Unity InfraSight - Architecture AR Multi-Plateforme

## Résumé

Le client Unity doit rester découpé en noyau commun et adaptateurs plateforme.
La connexion avec l'agent, le parsing des QR codes, les DTOs réseau et
les visualisations machines/containers ne doivent pas dépendre du matériel.

Les différences plateforme doivent rester aux bords du système :

- Meta Quest 3 / 3S : scan QR via Meta MRUK / `MRUKTrackable`.
- Android AR non-Meta : scan QR via AR Foundation / ARCore, image caméra CPU et ZXing.

`UNITY_ANDROID` ne suffit pas pour choisir l'implémentation : Meta Quest et
Android AR sont tous deux des builds Android. Le projet utilise donc
des symboles explicites :

- `INFRASIGHT_META_QUEST`
- `INFRASIGHT_ANDROID_AR`

## Découpage

### Core agnostique

Le core ne doit pas référencer Meta, ARCore, AR Foundation ou ZXing directement
sauf via interfaces.

Responsabilités core :

- parser les payloads QR avec `ServerConnectionClient.TryParseConnectionInfo`;
- construire l'endpoint avec `ServerConnectionClient.BuildEndpoint`;
- connecter/reconnecter via `ServerConnectionClient`;
- ignorer les scans doublons par endpoint;
- appliquer les snapshots `ServerDataPayload`;
- instancier et mettre à jour les prefabs de visualisation.

Composants principaux :

- `IQrScanProvider` : contrat de scan QR agnostique.
- `QrScanProviderBehaviour` : base Unity pour providers de scan.
- `InfraSightConnectionOrchestrator` : orchestration QR -> WebSocket -> payloads.
- `InfraSightMachineVisualizationManager` : création et mise à jour des objets affichés.
- `InfraSightQrClient` : point d'entrée scene-friendly pour brancher un provider QR.

### Contrat QR commun

Le scan QR doit exposer le même résultat quelle que soit la plateforme :

```csharp
public interface IQrScanProvider
{
    event Action<QrScanResult> QrDetected;
    bool IsSupported { get; }
    void StartScanning();
    void StopScanning();
}
```

`QrScanResult` contient :

- `Payload` : texte brut du QR code;
- `Pose` : position/rotation où placer la visualisation.

Le provider ne connecte jamais l'agent directement. Il émet seulement un QR détecté.

## Plateformes

### Meta Quest

Provider :

- `MetaQuestQrScanProvider`
- compile sous `INFRASIGHT_META_QUEST` ou symbole Unity/Meta équivalent.
- encapsule `MRUKTrackable`, `MarkerPayloadString` et `OVRAnchor.TrackableType.QRCode`.

Scène/profil :

- conserve rig Meta/MRUK actuel;
- conserve callbacks de QR tracking;
- garde les mêmes prefabs machine/container/feedback.

Manifest Quest :

- garde `com.oculus.intent.category.VR`;
- garde `android.hardware.vr.headtracking` required;
- garde `com.oculus.supportedDevices`;
- garde metadata Horizon/Meta nécessaires.

### Android AR non-Meta

Provider :

- `AndroidArQrScanProvider`
- compile sous `INFRASIGHT_ANDROID_AR`;
- utilise `ARCameraManager.TryAcquireLatestCpuImage`;
- convertit l'image caméra à cadence limitée;
- décode le QR avec `ZXing.Net`;
- émet `QrDetected(payload, pose)`.

Scène/profil :

- `AR Session`;
- `XR Origin`;
- `AR Camera Background`;
- `ARCameraManager`;
- `InfraSightQrClient` + `AndroidArQrScanProvider`;
- mêmes prefabs `MachineInfo`, `Cube`, `FeedBack`.

Manifest Android AR :

- retire toute exigence Oculus/VR;
- garde `android.permission.CAMERA`;
- garde `android.permission.INTERNET`;
- garde feature caméra/ARCore selon cible.

Packages :

- `com.unity.xr.arfoundation`;
- `com.unity.xr.arcore`;
- `ZXing.Net` contrôlé dans `Assets/Plugins/ZXing`.

## Règles de Maintenance

- Ne pas ajouter de `using Meta.*`, `OVR*`, `ARFoundation`, `ARCore` ou `ZXing`
  dans le core.
- Ne pas brancher `UNITY_ANDROID` directement sur une logique métier :
  utiliser `INFRASIGHT_META_QUEST` ou `INFRASIGHT_ANDROID_AR`.
- Garder `ServerConnectionClient`, `QrConnectionInfo` et `ServerDataPayload`
  comme contrat réseau source de vérité.
- Garder les nouveaux scanners comme adaptateurs : scan -> event, puis core connecte.
- Garder les visualisations communes : une nouvelle plateforme ne doit pas
  dupliquer `MachineVisualizationManager`.
- Garder les manifests séparés par profil/build, car Quest et Android AR ont
  des exigences incompatibles.

## Build et Setup

Dans Unity, les menus Editor dédiés servent de point d'entrée :

- `InfraSight/Platform/Configure Meta Quest`
- `InfraSight/Platform/Configure Android AR`
- `InfraSight/Platform/Create Android AR Scene`

Le postprocessor Android applique le manifest adapté selon le symbole actif.

Configuration attendue :

- Quest : `INFRASIGHT_META_QUEST`, scène Meta/MRUK.
- Android AR : `INFRASIGHT_ANDROID_AR`, scène AR Foundation/ARCore.

## Tests et Validation

Validation rapide :

```powershell
cd infrasight-client
dotnet build .\infrasight-client.sln --no-restore
```

Scénarios à garder couverts :

- parsing QR JSON `{ "name": "...", "ip": "...", "port": 8080 }`;
- parsing URL `ws://host:8080/ws`;
- parsing `host:8080`;
- scan doublon d'un endpoint ne crée pas de deuxième connexion;
- provider fake peut émettre un QR sans dépendre de Meta ou ARCore;
- Quest compile sans provider Android AR actif;
- Android AR compile sans provider Meta actif;
- Quest APK contient manifest Oculus;
- Android AR APK ne contient pas `com.oculus.intent.category.VR`;
- Android AR APK ne force pas `android.hardware.vr.headtracking required=true`;
- QR LAN `{"name":"Local Agent","ip":"<LAN_IP>","port":8080}` connecte l'agent
  et affiche les mêmes objets.

## Références

- [Unity conditional compilation](https://docs.unity.cn/2022.3/Documentation/Manual/PlatformDependentCompilation.html)
- [Unity Android permissions](https://docs.unity.cn/Manual/android-permissions-in-unity.html)
- [AR Foundation](https://docs.unity.cn/Packages/com.unity.xr.arfoundation%404.0/manual/)
- [AR camera CPU image](https://docs.unity.cn/Packages/com.unity.xr.arfoundation%404.2/manual/cpu-camera-image.html)
- [Meta Quest passthrough camera constraints](https://developers.meta.com/horizon/documentation/unity/unity-pca-overview/)
