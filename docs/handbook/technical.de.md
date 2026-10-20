# TransportOrders - Technische Dokumentation

Ausgabe 1.1 | 12. Oktober 2026 | Deutsch

## 1. Geltungsbereich und Referenzstand

Diese Dokumentation beschreibt den aktuellen Quellcode und die Pakete `recipes-api` / `recipes-client`. Ein installierter Dienst kann einen älteren Stand verwenden. Vor der Annahme, dass Alarmierung oder Datensicherung aktiv sind, den Pfad seiner ausführbaren Datei prüfen. Das Projekt ist ein unabhängiger Materialflussdemonstrator für die Aluminiumextrusion. Eine zertifizierte Kompatibilität mit MCVIS besteht nicht.

Implementiert sind ein persistentes Anlagenmodell, Transportplanung, simulierte Ofenbehandlung, Verpackung und Korbwiederverwendung, dauerhafte simulierte SPS-Kommunikation, Windows-authentifizierte Bedienbefehle, WPF in EN/DE, Rollen, Alarme, Online-Datensicherung und ein Inbetriebnahmeprofil zur Ansicht. Ein realer SPS-Treiber, freigegebene Produktionsrezepte, ERP-Anbindung und integrierter Barcode-/Etikettendruck fehlen. Vorhanden sind ein Eingabefeld für gelesene Codes und Etikettentext. Französisch ist eine Dokumentationssprache, keine Oberflächensprache.

Der geprüfte Referenzstand umfasst 338 Tests: 89 Domänen-, 58 Anwendungstests und 191 Integrationstests. Softwaretests ersetzen keine Abnahme einer physischen Anlage.

## 2. Architektur und Zuständigkeiten

| Komponente | Zuständigkeit |
| --- | --- |
| Core | Entitäten, Zustandswechsel, Regeln, Planung, SPS-Austauschmodell und Alarmerkennung; keine WPF-/SQLite-Referenz. |
| Data | EF-Core-/SQLite-Repositories, Migrationen, Demo-Initialisierung, SPS-Ausgangs-/Eingangsdaten und Online-Sicherung. |
| API | ASP.NET-Core-Dienst, Windows-Authentifizierung, Berechtigungen, serialisierte Änderungen und Hintergrunddienste. |
| App | .NET-8-WPF-Client für API-Zustandsbilder und Befehle; referenziert Core, nicht Data. |

Der Client liest `/api/client-state` ungefähr alle zwei Sekunden, sofern keine Hauptaktion beschäftigt ist. Er öffnet die Serverdatenbank nicht. `PlantConnection` hält eine Serveradresse und bindet den Client an `PlantId`; `RemotePlantRepository` erlaubt Lesezugriffe und verweigert lokale Änderungen. Verwendet werden die Windows-Anmeldedaten der Sitzung. Automatische HTTP-Weiterleitungen und eine Umgehung der Zertifikatsprüfung sind nicht aktiviert.

`PlantOperationGate` serialisiert Bedienänderungen, Laufzeitaktionen und Alarmsynchronisation innerhalb eines API-Prozesses. `PlantDriverLease` hält `<Datenbank>.driver.lock` während der Laufzeit und verhindert einen zweiten automatischen Ablauf für dieselbe lokale Datenbank. Eine vorhandene Sperrdatei allein beweist keinen aktiven Besitzer. Dies ist eine lokale Prozesssperre, keine verteilte Clustersperre.

## 3. Datenmodell und Regeln

| Entität | Aufgabe / Regel |
| --- | --- |
| Basket / MaterialLot / PlantLocation | Physischer Korb, Inhalt, Position und Zielreservierung; Positionen werden nicht bei jeder Abfrage aus dem letzten Auftrag abgeleitet. |
| TransportOrder / Transport | Ein Transportabschnitt und seine Ladung; Verlauf mit Benutzer, Zeit und Begründung. |
| PlantEquipment | Betriebsart, Zustand, aktive Operation und Version. Störung, Wartung oder Handbetrieb können die Automatik blockieren. |
| OvenRecipe / OvenCycle / OvenCycleLoad | Legierungskompatibilität und Rezeptkopie für die behandelte Beladungsfolge. |
| PackedPackage / BasketReturn | Paket und Qualitätsstatus sind unabhängig vom wiederverwendbaren Korb und seinem Rücktransport. |
| PlcCommand / PlcInbox / PlcSimulatorExecutions | Dauerhafter Auftrag, geprüfte Empfangsereignisse und deduplizierte simulierte Ausführung. |
| PlantAlarm / AlarmEvent | Ein Alarmereignis mit Kommen, Gehen, Quittierung und Verlauf. |

Eine Wiederbeladung erhöht `ContentSequence`. Ein früherer Zyklus oder ein altes Paket bestätigt keine neue Beladung. Eine Korbladung enthält ein Materiallos und wird zu einem Paket; Aufteilen oder Mischen wird nicht unterstützt. Der Abtransport prüft Quelle, Zielkapazität, Reservierungen, Betriebsmittel und Ofenkompatibilität. Ankunft und Anwendung der Rückmeldung werden gemeinsam in einer Transaktion gespeichert.

Historische Feldnamen bleiben bestehen: `HeatNumber` bezeichnet die Materialcharge, `PieceNumber` den Korb, `ParentPieceNumber` seine Bolzencharge und `BasketContentSequence` die Beladungsfolge. Einheiten: Tonnen, Lademeter, Grad Celsius und simulierte Minuten. Betriebliche Zeitstempel werden in UTC gespeichert; Schichten verwenden `Plant:TimeZone`, Alarmzeiten die lokale Windows-Zeit des Clients.

Eine volle Ladung nutzt mindestens 90 % der verfügbaren Ladelänge. Länge und Nutzlast dürfen nicht überschritten werden; Leerfahrten und gemischte Routen werden abgelehnt. Eine Teilladung benötigt einen erlaubten dokumentierten Grund. Auftragszustände: `Planned`, `OnHold`, `InTransit`, `Done`, `Cancelled`. Dispatch führt zu `InTransit`; `Done` und `Cancelled` sind endgültig.

Ofenphasen: Aufheizen, Halten, Abkühlen und Behandlung beendet. Der Zyklus übernimmt Rezeptparameter und Beladungsidentitäten bei Start. Ein Ereignis erhöht die simulierte Zeit um eine Minute. Stillstand des Dienstes wird nicht als Behandlung nachgeholt. Temperaturen sind interpolierte Demowerte, keine Messwerte. Sicht-/Mengenprüfung bestimmen Freigabe, Etikettentext und Versand. Automatische Demo-Qualitätsprüfungen sind simuliert.

Rezeptverwaltung: GET /api/recipes und POST /api/recipes/{id}/create, revise, validate oder archive. Änderungen verlangen die Rolle Supervisor, Anlagenkennung, einen Grund und bei vorhandenen Rezepten die erwartete Version. Gemeinsame Ausführungssperre und EF-Versionsprüfung verhindern veraltete Änderungen. RecipeEvents speichern vollständige Parameter; Zyklen speichern RecipeRevision. Migration 20261012172347_VersionedOvenRecipes übernimmt vorhandene Rezepte als validierte Revision 1 und importiert den Ausgangsstand einmalig. Rezepte werden nicht gelöscht. Parameter bleiben illustrative Simulationswerte.

## 4. SPS-Austausch und Nebenläufigkeit

Transportreservierung und SPS-Auftrag werden gemeinsam gespeichert. Ein Auftrag enthält stabile GUID, Vertragsversion, Betriebsmittel, Operation, Quelle, Ziel, unveränderliche Nutzdaten und Hash. Der Simulator speichert Ausführungen je GUID. Ein erneuter Sendeversuch verwendet dieselbe GUID und dieselben Nutzdaten.

| Zustand | Bedeutung |
| --- | --- |
| Pending | Eingereiht, noch nicht gesendet. |
| AwaitingAck | Sendeversuch gespeichert; Rückmeldung ausstehend. |
| Accepted | Annahme bestätigt; Ankunft nicht nachgewiesen. |
| Completed | Abschlussmeldung empfangen; Anwendung eventuell zurückgestellt. |
| Applied | Ankunft und Rückmeldung gemeinsam verbucht. |
| TimedOut | Frist abgelaufen; Reservierung bleibt erhalten. |
| Rejected | SPS-Ablehnung; Bedienprüfung erforderlich. |

Die Frist beträgt 15 Sekunden, höchstens drei Sendeversuche. Ein Timeout löst keinen automatischen Neuversand aus. Geprüft werden doppelte Ereignis-IDs, abweichende Route/Betriebsmittel/Hash und alte Sequenzen. Eine gültige verspätete Meldung kann einen Timeout ohne Neuversand beheben. Bei Störung kann eine gespeicherte Abschlussmeldung bis zur Verfügbarkeit zurückgestellt werden. Manuelle Ankunft ist eine gesondert protokollierte Abstimmung, keine erfundene Maschinenquittung.

GUID-Versionen und `OrderRevision` schützen gegen veraltete Clientdaten. Die Auftragsrevision ist ein SHA-256-Abbild relevanter Zustands- und Verlaufsdaten. `ExpectedPlantId` prüft die Installation; für ältere Befehlsclients bleibt die bisherige Datenbankpfadprüfung erhalten. Eine verlorene HTTP-Antwort kann auf eine bereits gespeicherte Aktion folgen. Vor einer weiteren Aktion Zustand und Verlauf neu lesen.

## 5. Identität, Rollen und Betriebsprofile

Negotiate authentifiziert den Windows-Principal. Die handelnde Identität stammt vom Principal, nicht aus einem JSON-Benutzernamen. Fachliche Lesezugriffe sind authentifiziert. `/health` ist eine anonyme HTTP-Erreichbarkeitsprüfung, kein Nachweis eines gesunden Produktionsablaufs.

| Rolle | Änderungsrechte |
| --- | --- |
| Quality | Paketprüfung; Aufträge sperren/freigeben. |
| Logistics | Körbe neu beladen; stornieren; Transporte starten und Ankunft manuell bestätigen. |
| Maintenance | Anlagenzustände; SPS-Einstellungen, Wiederholungen und Abstimmung; Alarme quittieren. |
| Supervisor | Alle Bedienbefehle einschließlich Demoschritten. |

Rollen sind kombinierbar. `Access:Roles` ordnet bestehende Benutzer/Gruppen zu, legt sie jedoch nicht an. Bei `UseRoles=true` verleihen ältere Positivlisten keine zusätzlichen Rechte. Alarmquittierung verwendet `equipment.control`, einschließlich beibehaltener Legacy-Berechtigung. Ohne wirksame Rechte ist nur Ansicht erlaubt. Ein Rechteverlust deaktiviert Clientbefehle, stoppt aber keinen unabhängigen zentralen Ablauf.

Standard ist `Plant:Mode=Demo`. `Commissioning` verlangt explizite Werte für `Database:Path` und `Plant:Id`, verweigert einen aktivierten Runtime und initialisiert nur das Schema ohne Demodaten. Ändernde HTTP-Anfragen werden abgelehnt; der Client erhält keine Änderungsrechte. `Live` und unbekannte Profile werden abgelehnt: physischer Betrieb ist nicht implementiert. Für Inbetriebnahme eine getrennte, geprüfte Datenbank und Konfiguration verwenden.

## 6. API-Vertrag

JSON liefert Enum-Werte als Text. Zustandsbildversion ist 1; optionale Felder erweitern sie um Alarme und Betriebsprofil. Übliche Antworten: 400 ungültige Eingabe, 401 nicht authentifiziert, 403 nicht berechtigt/nur Ansicht, 404 unbekannte Referenz, 409 veralteter Zustand, falsche Installation oder blockierte Aktion.

| Leseendpunkt | Aufgabe |
| --- | --- |
| /api/client-state | Physisches Zustandsbild, Rechte, Runtime, Alarme und Profil. |
| /api/access; /api/runtime; /api/backups/status | Wirksame Rechte, Laufzeitdiagnose, Sicherungsergebnis. |
| /api/orders; /api/orders/{id}; /api/orders/{id}/history | Aufträge und Verlauf; numerische ID bei diesen Einzelrouten. |
| /api/orders/by-number/{number}; /api/transports; /api/load-plan | Nummernsuche, Transporte und berechnete Vorschläge. |
| /api/plant; /api/schedule; /api/plc | Anlage, Ablaufkandidaten und dauerhafte SPS-Aufträge. |
| /api/packages/{number}/label | Freigegebener Etikettentext. |
| /api/heats/{heatNumber}/genealogy; /api/shift-summary | Materialgenealogie und Schichtkennzahlen. |

POST-Befehle: `/api/equipment/{id}/control`, `/api/packages/{id}/inspection`, `/api/baskets/{id}/reload`, `/api/orders/{id}/state` (Auftragsnummer), `/api/transports/dispatch`, `/api/transports/{id}/manual-arrival`, `/api/plc/simulation/settings`, `/api/plc/commands/{id}/retry`, `/api/plc/commands/{id}/manual-arrival`, `/api/simulation/step`, `/api/alarms/{id}/acknowledge`. Geprüft werden Rolle, aktuelle Version/Revision und Installationsidentität; erforderliche Begründungen sind Pflicht. Die Request-Records in Core/API definieren die genauen Nutzdaten. Swagger ist nur in Development aktiviert.

## 7. Installation und Konfiguration

Für den Build: Windows und .NET-8-SDK. `win-x64 --self-contained true` enthält die Laufzeit. Der installierte Dienst heißt `TransportOrders.Runtime`, normalerweise unter `NT AUTHORITY\LocalService`, mit verzögertem automatischem Start und konfigurierten Wiederanläufen. Dienständerungen erfordern erhöhte Windows-Rechte.

Für die Erstinstallation mit `Install-WindowsService.ps1` PowerShell 7 verwenden: `Path.IsPathFullyQualified` steht in Windows PowerShell 5.1 nicht zur Verfügung. Geprüfte Aktualisierungs-/Diagnoseskripte können Windows PowerShell 5.1 verwenden. Für eine neue Demo das gewählte Datenbankverzeichnis anlegen, die API einmal mit explizitem `--Database:Path` starten und nach Initialisierung mit Ctrl+C stoppen. Eine ungeprüfte operative Datenbank nicht durch Demo-Initialisierung öffnen.

```powershell
dotnet build TransportOrders.sln
dotnet test TransportOrders.sln
dotnet publish src/TransportOrders.Api -c Release -r win-x64 --self-contained true -o artifacts/recipes-api
dotnet publish src/TransportOrders.App -c Release -r win-x64 --self-contained true -o artifacts/recipes-client
```

Einen vorhandenen Dienst mit `scripts/Deploy-EquipmentApi.ps1`, Benutzer, Rolle und Paketpfad aktualisieren. Das Skript übernimmt die Konfiguration und versucht bei fehlgeschlagener Bereitschaftsprüfung den alten Programmpfad wiederherzustellen. Datenbankmigrationen werden nicht zurückgerollt. Vor einem Upgrade eine geprüfte Sicherung erstellen. Erstinstallation verlangt eine vorhandene initialisierte Datenbank; den Pfad vor `Install-WindowsService.ps1` prüfen.

```powershell
# Erhöhte Shell; das Beispielkonto durch die vorgesehene Windows-Identität ersetzen.
.\scripts\Deploy-EquipmentApi.ps1 -OperatorName 'DORNBIRNTATA\moham' -Role Supervisor -PublishPath '.\artifacts\recipes-api'
.\scripts\Check-PlantBackup.ps1
```

| Einstellung | Bedeutung |
| --- | --- |
| Database:Path | Expliziter absoluter Serverdatenbankpfad im Windows-Dienst. |
| Plant:Id / Name / Mode / TimeZone | Stabile Identität, Anzeigename, Profil und Schichtzeitzone. |
| Runtime:Enabled / LogDirectory | Simulierter Ablauf und Serverprotokolle. |
| Access:UseRoles / Roles | Windows-Benutzer-/Gruppenzuordnungen. |
| Backup:Enabled / Directory / IntervalHours | Sicherung beim Start und periodisch; 1-168 Stunden, Standard 24. |
| Kestrel:Endpoints | Lokales HTTP, entferntes HTTPS und Zertifikat. |

Client: `TRANSPORTORDERS_SERVICE_URL`, Standard `http://localhost:5080`, sowie optional `TRANSPORTORDERS_PLANT_ID` für Bindung vor Erstkontakt. Entfernte Adressen benötigen HTTPS. Zertifikat und Servername müssen passen; das Zertifikat muss vertrauenswürdig sein. LocalService benötigt Leserecht auf genau seinen privaten Schlüssel. Demo-HTTPS verwendet DORNBIRNTATA:5443; die Firewallregel beschränkt sich auf Privatprofil/lokales Subnetz. Das Netzwerkprofil wird nicht automatisch geändert. Ein zweiter Arbeitsplatz benötigt Zertifikatsvertrauen, funktionierende Windows-Authentifizierung und einen Verbindungstest.

## 8. Speicherung, Alarme, Sicherung und Wiederanlauf

Die konfigurierte installierte Datenbank ist `C:\ProgramData\TransportOrders\extrusion.db`. Entwicklungs-Fallbacks bestimmt `DatabasePaths`, nicht WPF. Sprache und Clientprotokolle liegen in `%LOCALAPPDATA%\TransportOrders.Client`. Serverprotokolle sind täglich, standardmäßig mit 14 aufbewahrten Dateien. Datenbank, Konfiguration, Programme, private Schlüssel und Sicherungen durch Windows-Dateirechte schützen.

Eine aktive Bedingung erzeugt nicht bei jeder Abfrage eine neue Meldung. Quittierung protokolliert Benutzer/Kommentar und beseitigt die Bedingung nicht. Gehen und erneutes Kommen ergeben getrennte Ereignisse bzw. Vorkommnisse. Laufzeit-/Sicherungsfehler hängen von den jeweiligen Workerdiagnosen ab. WPF zeigt einen unerreichbaren Dienst lokal an; der ausgefallene Dienst kann seine eigene Nichterreichbarkeit nicht in einer unerreichbaren Datenbank speichern.

Die Online-Sicherung verwendet die SQLite-Backup-API, übernimmt bestätigte WAL-Daten und erzeugt `.db` plus Manifest `.db.json`. Prüfung: SHA-256, Größe, Installation, SQLite-Integrität, Fremdschlüssel und unterstützte Migrationsfolge. Sicherungen erfolgen beim Start und im konfigurierten Intervall; alte Kopien werden nicht automatisch gelöscht. Für Schutz vor Plattenverlust auf getrennten Speicher kopieren.

`Backup-Plant.ps1` bietet backup, verify und `restore-copy`. Wiederherstellung verlangt einen unbenutzten Zielpfad ohne SQLite-Journaldateien und ersetzt die aktive Datenbank nicht. Die Kopie bei deaktiviertem Runtime prüfen und reale Positionen abstimmen, bevor sie ausgewählt wird. Historische Kompatibilität ist begrenzt: ältere Migrationen enthalten Demo-Resets; `EnsureCreated`-Datenbanken werden gesondert behandelt. Beliebige operative Altdatenbanken sind kein unterstützter Import.

## 9. Prüfung und Wartung

Lösungstests, `Check-PlantBackup.ps1` und das isolierte `Test-RuntimeRecovery.ps1` verwenden. Der Wiederanlauftest arbeitet mit eigener Datenbank und Port 5099, beendet Prozesse abrupt und prüft einen Sendeversuch sowie eine Ankunftsanwendung. Den installierten Dienst stoppt er nicht. `New-CommissioningProfile.ps1` erstellt ein leeres Ansichtsprofil auf localhost:5096.

Diagnose: Dienststatus/-pfad, authentifizierte `/api/runtime` und `/api/backups/status`, Client-/Serverlogs, `PlantId`, Zertifikatsablauf und Rollen. Fehler 1053 verlangt Prüfung von Start, Konfiguration, Programm-/Datenbankzugriff und Logs. Ein längeres Timeout ersetzt diese Diagnose nicht.

Quellcode-Einstiege: `Api/Program.cs`, `PlantRuntimeWorker`, `PlantAlarmWorker`, `PlantBackupWorker`, Core `PlantCommands`, `PlcExchange`, `ProductionSchedule`, Data `AppDbContext`, Repositories und `DatabaseBackup`, App `PlantConnection` und ViewModels. Fachleitfäden stehen im Dokumentationsindex. Physische SPS-Abnahme, freigegebene Rezepte, ERP, Drucker und längere Anlagenversuche bleiben eigenständige Arbeiten.
