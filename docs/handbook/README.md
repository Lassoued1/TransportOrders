# TransportOrders documentation

Edition 1.1 - 12 October 2026. Includes versioned simulation oven recipes. Maintained sources and printable PDFs cover the same current source/package baseline. Check the installed service version separately.

| Language | Technical reference | User manual |
| --- | --- | --- |
| English | [Markdown](technical.en.md) / [PDF](../../output/pdf/TransportOrders-TECHNICAL-EN.pdf) | [Markdown](user.en.md) / [PDF](../../output/pdf/TransportOrders-USER-EN.pdf) |
| Deutsch | [Markdown](technical.de.md) / [PDF](../../output/pdf/TransportOrders-TECHNICAL-DE.pdf) | [Markdown](user.de.md) / [PDF](../../output/pdf/TransportOrders-USER-DE.pdf) |
| Français | [Markdown](technical.fr.md) / [PDF](../../output/pdf/TransportOrders-TECHNICAL-FR.pdf) | [Markdown](user.fr.md) / [PDF](../../output/pdf/TransportOrders-USER-FR.pdf) |

The application itself supports English and German. French is a documentation language. The documents distinguish Demo, read-only Commissioning and the physical/ERP functions that are not implemented.

Generate the PDFs with Python, ReportLab and Windows Arial/Consolas fonts:

```powershell
python scripts/Build-Handbook.py
```

For layout validation, render every PDF with Poppler (`pdftoppm -r 90 -png`) into `tmp/pdfs`, using the PDF stem as the output prefix, then run `python scripts/Validate-Handbook.py`. The validator checks outlines, destinations, text bounds and local links and builds review sheets. Visually inspect the pages before distribution; temporary renders can then be removed.

Specialized sources: [oven recipes](../oven-recipes.fr.md), [network clients](../network-clients.fr.md), [roles](../operator-roles.fr.md), [alarms](../alarms.fr.md), [backups](../backup-restore.fr.md), [recovery](../runtime-recovery.fr.md), [commissioning](../commissioning.fr.md), [PLC exchanges](../plc-exchanges.fr.md), [Windows service](../windows-service.fr.md). The handbook is the consolidated reference; older specialized notes may include historical implementation details.
