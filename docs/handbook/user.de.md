# TransportOrders - Benutzerhandbuch

Ausgabe 1.1 | 12. Oktober 2026 | Deutsch

## 1. Vor dem Start

TransportOrders verfolgt Aluminium-Extrusionskörbe von den Pressenausläufen über Auslagerungsöfen bis zur Verpackung. Freigegebene Pakete gelangen zum Versandpuffer; leere Körbe werden zurückgeführt und erneut beladen. Im aktuellen Demo-Modus sind Betriebsmittel, Ofenzeiten und SPS-Rückmeldungen simuliert. Eine reale Maschine ist nicht angeschlossen. Commissioning erlaubt nur Ansicht und erzeugt keine Demodaten.

Dieses Handbuch beschreibt den aktuellen `recipes-client` und die dazugehörige API. Ein älterer Dienst kann neue Funktionen noch nicht bereitstellen. Die Oberfläche unterstützt English und Deutsch. Die französische Dokumentation bedeutet keine französische Oberfläche.

Erforderlich sind der laufende zentrale Dienst und ein dort erkanntes Windows-Konto. Den Client über `scripts/Start-NetworkClient.ps1` für den konfigurierten HTTPS-Server DORNBIRNTATA starten oder die vom Administrator vorgegebene Verbindung verwenden. Ein entfernter Arbeitsplatz muss dem Serverzertifikat vertrauen. Eine Datenbankkopie ist dort nicht erforderlich.

## 2. Start, Sprache und Berechtigungen

1. Client starten und die erste Aktualisierung abwarten.
2. Angezeigte Windows-Identität/Rollen und Dienststatus kontrollieren.
3. **English** oder **Deutsch** auswählen. Die Sprache wird pro Benutzer gespeichert.
4. Bei fehlender Verbindung können angezeigte Daten veraltet sein; Befehle sind deaktiviert. Nach Wiederherstellung den aktualisierten Zustand vor einer Aktion prüfen.

| Rolle | Wichtige Aktionen |
| --- | --- |
| Qualität (Quality) | Pakete prüfen; Aufträge sperren/freigeben. |
| Logistik (Logistics) | Körbe neu beladen; stornieren; Transporte starten und Ankunft bestätigen. |
| Instandhaltung (Maintenance) | Anlagenzustände; SPS-Diagnose, Wiederholung und Abstimmung; Alarme quittieren. |
| Schichtleitung (Supervisor) | Alle Aktionen einschließlich Demoschritten. |
| Keine wirksamen Rechte | Nur Ansicht. |

Eine deaktivierte Schaltfläche kann auf fehlende Rechte, ungeeigneten Materialzustand, eine laufende Aktion oder Commissioning hinweisen. Rollen umgehen keine Ablaufregeln. Berechtigungen beim Administrator beantragen; eigene Rollenzuweisungen sind in der Anwendung nicht möglich.

## 3. Navigation und Anlagenzustand

| Deutscher Reiter | Englischer Reiter | Inhalt |
| --- | --- | --- |
| Anlagenübersicht | Plant overview | Stationen, Kapazität, Körbe, Betriebsmittel und Ofenzyklen. |
| Verpackung & Korbrücklauf | Packing & basket reuse | Paketprüfung, Etikettentext und erneute Korbbeladung. |
| Produktionsplanung | Production schedule | Nächste zulässige Aktion und Blockierungsgründe. |
| SPS-Kommunikation | PLC exchanges | Auftragszustände, Wiederholungen, Fristen und Verlauf. |
| Alarme | Alarms | Aktive/gegangene Meldungen und Quittierungen. |
| Ofenrezepte | Oven recipes | Entwürfe, Simulationsvalidierung, Revisionen und Archiv. |
| Aufträge & Rückverfolgung | Orders & traceability | Aufträge, Verlauf, Ladungsplanung und Chargenbezug. |

Eine Zielreservierung ist eine vorgesehene Belegung, kein Ankunftsnachweis. Ein fahrender Korb belegt die gemeinsame Transferressource. Korbnummer, Materiallos und Beladungsfolge zusammen prüfen: derselbe Korb erhält bei Wiederverwendung eine neue Ladung. Ein Paket behält die Identität seiner ursprünglichen Ladung.

## 4. Demonstration bedienen

Diese Funktionen gehören zum Demo-Modus. **Nächster Schritt / Next step** fordert eine zulässige simulierte Aktion an, wenn kein zentraler Runtime aktiv ist. Besitzt der Dienst bereits den automatischen Ablauf, meldet die Antwort dessen Zuständigkeit. Das Schließen oder Pausieren von WPF stoppt den unabhängigen Dienst nicht.

Bei einer vom Arbeitsplatz gesteuerten Demo fordert **Demo starten / Run demo** regelmäßig Schritte an. Vor Einstellungen oder manuellen Qualitätsprüfungen diesen Modus beenden. Einen unabhängigen Runtime für einen beaufsichtigten Versuch durch den Administrator stoppen lassen. Eine Desktop-Pause ist kein Maschinenstopp.

Beispielroute: Presse -> Ofen -> Verpackung. Aufheizen, Halten und Abkühlen müssen vor dem Ofenaustransport abgeschlossen sein. Simulierte Temperaturen sind keine Messwerte. Gesperrte und stornierte Beispielaufträge laufen nicht normal weiter. Eine Freigabe lässt den gesperrten Auftrag fortsetzen; Stornierung ist endgültig.

## 5. Betriebsmittel und Öfen

1. In Anlagenübersicht das richtige Betriebsmittel auswählen.
2. Betriebsart, Zustand, aktive Operation und Ofenphase lesen.
3. Mit entsprechender Berechtigung Automatik/Handbetrieb und betriebsbereit/Störung/Wartung für den Demoversuch wählen.
4. Einen nachvollziehbaren Grund eingeben und **Übernehmen** wählen.
5. Aktualisierten Zustand und Verlauf kontrollieren.

Störung, Wartung oder Handbetrieb können die Automatik blockieren. Erst nach Prüfung auf betriebsbereit/automatisch zurückstellen. Ein Zyklus überlebt einen Neustart; die Stillstandszeit wird nicht als Behandlung angerechnet.

Unter Ofenrezepte erstellt die Schichtleitung ein Rezept mit unveränderlicher Kennung, vierstelliger Legierung, Temperatur und positiven Zeiten für Aufheizen, Halten und Abkühlen. Grund eingeben und Entwurf speichern. Gespeicherten Entwurf auswählen, Prüfgrund eingeben und für die Simulation validieren. Nur validierte Rezepte starten neue Zyklen. Änderungen erzeugen eine neue Entwurfsrevision mit erneuter Validierung. Vor Validierung oder Archivierung geänderte Parameter speichern oder neu laden. Archivierte Rezepte bleiben im Verlauf und können nicht bearbeitet werden. Laufende Zyklen behalten ihre ursprüngliche Revision und Parameter. Der Verlauf erfasst Windows-Benutzer, Zeit, Grund und alle Parameter. Bei Konflikt das ausgewählte Rezept neu laden; die automatische Aktualisierung erhält ungespeicherte Eingaben.

Ein Versionskonflikt bedeutet, dass sich die Auswahl auf dem Server geändert hat. Ausgewählte Version aktualisieren, den neuen Zustand prüfen und die gewünschte Aktion erneut formulieren. Wiederholtes Anwenden erzwingt keinen veralteten Befehl.

## 6. Qualität, Verpackung und Wiederbeladung

Vor einer manuellen Paketprüfung automatische Desktopanforderungen beenden. Ein Paket im Zustand Wartet auf Prüfung oder Abgelehnt auswählen. Sicht- und Mengenprüfung eintragen, einen Grund angeben und **Prüfung erfassen** wählen. Beide Prüfungen müssen bestanden sein. Ablehnung verhindert Etikettenfreigabe und Versand; nach Korrektur ausdrücklich erneut prüfen. Automatische Demo-Prüfungen ersetzen keine reale Qualitätsprüfung.

Ein freigegebenes Paket zeigt rückverfolgbaren Etikettentext. Eine integrierte Druckeransteuerung oder grafische Barcodeerzeugung gibt es nicht. Paketversand und Leerkorbrücklauf sind voneinander unabhängig.

Zur Wiederbeladung einen zur Presse zurückgekehrten leeren Korb, Materiallos und Zielofen auswählen; Menge, Gewicht und Ladelänge eingeben. **Korb neu beladen** wählen und neue Beladungsfolge sowie neuen Auftrag prüfen. Die Werte sind Demoeingaben ohne Bestandsreservierung. Eine vorherige Ofenbehandlung gilt nicht für die neue Ladung.

## 7. Aufträge, Transporte und Rückverfolgung

Aufträge & Rückverfolgung verwenden, sofern der Reiter durch die Rolle freigegeben ist. Auftragsnummer suchen oder in das Codefeld einlesen; Auftrag und Verlauf prüfen. Chargensuche zeigt zugehörige Bewegungen; die Genealogie gruppiert verbundene Materialidentitäten.

Eine Qualitätssperre benötigt einen Grund und schließt den Auftrag von der Planung aus. Erst nach Qualitätsprüfung freigeben. Stornierung ist endgültig. **Ladungen planen / Plan loads** berechnet Vorschläge, löst aber allein keinen Transport aus. Route, Länge und Gewicht vor dem Start prüfen. Eine volle Ladung belegt mindestens 90 % der Ladelänge. Teilladungen erfordern einen zulässigen gespeicherten Grund. Leer- und Überlasttransporte werden abgelehnt.

Eine Ankunftsbestätigung betrifft den Transport und seine geladenen Aufträge. Eine manuelle Bestätigung dokumentiert die physische Abstimmung. In einer späteren realen Anlage die Position vorher nach freigegebener Vorgehensweise prüfen. Diese Aktion beweist keine empfangene SPS-Rückmeldung.

Tastenkürzel: F5 aktualisiert; Ctrl+F setzt den Suchfokus; Esc im Suchfeld leert die Suche. Neue Daten können Aktionen eines anderen Bedieners oder des Runtime zeigen.

## 8. SPS-Diagnose

Pending: eingereiht; AwaitingAck: gesendet; Accepted: angenommen, nicht angekommen. Completed: Abschlussmeldung empfangen, eventuell bis zur Anlagenverfügbarkeit zurückgestellt. Applied: Ankunft verbucht. TimedOut und Rejected erfordern Prüfung.

Im Demo-Modus darf Maintenance Rückmeldungen unterdrücken oder den simulierten Kanal trennen. Der Timeout beträgt 15 Sekunden; Zielreservierungen bleiben bestehen. Wiederherstellen der Antworten erlaubt den Test einer verspäteten Rückmeldung. Eine Wiederholung ist ausdrücklich zu begründen, behält die Auftrags-ID und ist auf drei Sendeversuche begrenzt. Ablehnung nicht blind wiederholen. Manuelle Abstimmung benötigt einen Grund und wird gesondert protokolliert.

Ist keine SPS konfiguriert, zeigt die Oberfläche dies an. Diese Einstellungen betreffen den Simulator, nicht die Verbindung zu einer physischen SPS.

## 9. Alarme quittieren

Im Reiter Alarme eine Meldung auswählen. Quelle, Bedingung, aktiv/gegangen, Zeitpunkt und Verlauf lesen. Maintenance oder Supervisor kann einen Prüfvermerk eingeben und quittieren. Gespeichert werden Windows-Benutzer und Kommentar. Die Quittierung behebt keine Störung und beendet keinen Transport. Eine Meldung kann danach aktiv bleiben. Erneutes Auftreten erzeugt eine neue unquittierte Meldung.

Arbeitsversion und Kommentar bleiben während der Aktualisierung erhalten. Bei einem Konflikt die aktuelle Meldung prüfen und **Ausgewählte Version aktualisieren** verwenden, bevor die überarbeitete Aktion übermittelt wird. Bei fehlender Alarmfunktion im Dienst oder Verbindungsverlust ist Quittieren deaktiviert. Laufzeit- und Sicherungsfehler erscheinen, wenn die entsprechenden Funktionen aktiviert sind.

## 10. Störungen, Beenden und Übergabe

| Beobachtung | Vorgehen |
| --- | --- |
| Verbindung fehlt | Serveradresse und Dienst mit Administration prüfen; angezeigte Daten können alt sein. |
| Zugriff verweigert / Befehl deaktiviert | Identität, Rolle, Ablaufzustand und Commissioning prüfen. |
| Zustandskonflikt | Aktualisieren und Verlauf lesen; Auswahl wurde möglicherweise anderweitig geändert. |
| Kein Transportfortschritt | Planungsgründe, Sperren, Kapazität, Behandlung und Betriebsmittel prüfen. |
| SPS-Timeout | Reservierungen beibehalten; Verbindung und Verlauf vor begründetem Neuversuch prüfen. |
| Antwort nach Aktion verloren | Zuerst aktualisieren: die Änderung kann bereits gespeichert sein. |
| Sicherungsalarm | Administration informieren; letzte erfolgreiche Sicherung vor Wartung prüfen. |

WPF normal schließen. Der unabhängige Runtime kann weiterarbeiten. Bei Schichtübergabe unquittierte Alarme, gesperrtes/abgelehntes Material, offene SPS-Aufträge und ungelöste manuelle Aktionen übergeben. Datensicherung/Wiederherstellung gehören zur technischen Dokumentation. Die Datenbank nicht löschen oder ersetzen, um die Anlage zurückzusetzen.
