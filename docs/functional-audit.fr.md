# Bilan fonctionnel - 12 octobre 2026

Vérification réalisée sur le service installé `TransportOrders.Runtime`, paquet `artifacts/recipes-api`, et le client `artifacts/recipes-client`. Mode actif : **Demo**. Les essais avec changements métier et arrêts brutaux ont utilisé des bases isolées. L’atelier installé n’a pas été réinitialisé et son service n’a pas été interrompu pour cet audit.

## Résultats vérifiés

- **338 tests réussis**, aucun échec ni test ignoré : 89 métier, 58 application et 191 intégration. Compilation des tests avec avertissements traités comme erreurs.
- Service en fonctionnement sous LocalService, démarrage automatique. Runtime activé, horodatage récent, `lastError = null`.
- HTTPS `https://DORNBIRNTATA:5443` répond avec validation normale du certificat et authentification Windows. Les données métier sont refusées sans authentification : HTTP 401.
- Le compte courant reçoit le rôle Supervisor. Client WPF ouvert et répondant ; son journal confirme la lecture des ordres et transports environ toutes les deux secondes.
- État persistant constaté : 12 paniers, dont 10 vides ; 10 colis expédiés ; 6 cycles terminés ; 35 commandes PLC au statut Applied ; 3 recettes validées en révision 1.
- L’ordonnanceur ne propose actuellement aucune action. Il indique un panier bloqué qualité et un panier associé à un ordre annulé. Cet état d’attente correspond aux règles du scénario.
- Aucune alarme dans l’instantané au moment du contrôle.
- Sauvegarde automatique activée, dernière exécution réussie, aucune erreur. La copie a été vérifiée avec le paquet actuel : identité du site, empreinte SHA-256, intégrité et séquence des huit migrations acceptées.
- Essai du paquet publié après **trois arrêts brutaux** : reprise réussie, commande appliquée une fois, une seule tentative d’envoi et un ordre récupéré. Le service installé n’a pas été modifié par ce test.

Les résultats directs sont consignés dans `artifacts/functional-audit-state.json`. Les tests sont consignés dans `artifacts/functional-audit-tests.log` et l’essai de reprise dans `artifacts/functional-audit-recovery.log`.

## Fonctions disponibles en simulation

| Fonction | État et preuve |
| --- | --- |
| Atelier et paniers | Positions, réservations et capacités persistantes. Parcours complet testé sans dépassement de capacité. |
| Transports et ordres | Planification, départ, arrivée, annulation, blocage et libération qualité testés. Les ordres suivants sont générés par le flux ; aucun éditeur général de création/import d’ordres n’a été trouvé dans le client. |
| Fours | Chauffage, maintien, refroidissement, suspension sur défaut/manuel et conservation des cycles après redémarrage testés. |
| Recettes | Création de brouillons, modification avec révision, validation pour simulation, archivage et historique testés. Un brouillon ne peut pas démarrer un cycle ; les cycles existants conservent leurs paramètres. Gestion réservée au Supervisor. |
| Emballage et qualité | Inspection, rejet/réinspection, approbation et expédition testés. L’interface fournit une étiquette textuelle traçable. |
| Réutilisation des paniers | Retour vide et rechargement testés. La nouvelle séquence de chargement empêche l’utilisation d’un ancien traitement comme preuve du nouveau. |
| Ordonnancement | Prochaines actions, priorités, contraintes d’alliage et motifs de blocage testés. L’instantané installé expose les blocages actuels. |
| Échanges PLC simulés | Acquittement, fin de mouvement, temporisation, réponse tardive, doublons, réessais limités et réconciliation manuelle testés. Aucun driver physique. |
| Alarmes | Création, disparition, récidive, acquittement et historique persistants testés. L’acquittement ne corrige pas le défaut source. |
| Droits opérateur | Matrice Quality/Logistics/Maintenance/Supervisor testée avec identités de test. Supervisor confirmé avec le compte Windows réel de cette installation. |
| Client WPF | Lectures et commandes via API. Affichage EN/DE et interactions testés ; rendu WPF sur thread STA. Le client installé répond et actualise ses données. L’audit n’a pas cliqué toutes les commandes sur la base installée. |
| Traçabilité | Historique d’ordres, lots, paniers, chargements, traitements, colis et changements opérateur testé. |
| Sauvegarde/reprise | Sauvegarde à chaud vérifiée sur cette installation. Restauration vers une copie et récupération PLC testées en isolation. |
| Documentation | Guides techniques et utilisateur EN/DE/FR, édition 1.1, avec recettes. Le français reste une langue documentaire. |

## Limites actuelles et fonctions absentes

| Sujet | Limite |
| --- | --- |
| Automates et machines réels | Pas de driver physique ni de correspondance de signaux, capteurs ou interverrouillages validée. Le programme refuse le mode Live. |
| Traitement thermique industriel | Températures interpolées et temps simulés. Pas de mesures réelles, de régulation du four ni de recettes industrielles approuvées. |
| Intégration ERP/stock | Aucune interface ERP ; les rechargements n’effectuent pas une réservation de stock industriel. |
| Impression et codes-barres | Champ de saisie/scanner clavier et texte d’étiquette présents. Impression intégrée et génération graphique de codes-barres absentes. |
| Langue française du client | Non implémentée. Les interfaces sont EN/DE. |
| Mélange et fractionnement matière | Modèle actuel : un chargement porte un lot et produit un colis. Mélanges et fractionnements ne sont pas pris en charge. |
| Pause globale | Fermer ou arrêter la démonstration du client n’arrête pas le runtime central. L’arrêt central relève actuellement de l’administration du service. |
| Mise en service réelle | Profil Commissioning disponible en consultation, sans données de démonstration et sans commandes. Il n’effectue pas le pilotage physique. |
| Haute disponibilité | Un service et une base SQLite locale ; absence de basculement serveur ou de coordination de cluster. |
| Gestion des sauvegardes | Pas de purge automatique ni de remplacement automatique de la base active par une restauration. Conservation hors du disque serveur à organiser. |

## Points non validés ou à préparer

1. **Deuxième poste opérateur.** HTTPS fonctionne depuis ce PC. Aucun essai depuis un deuxième PC n’a été réalisé. Le Wi-Fi est actuellement en profil Public tandis que la règle `TransportOrders demo HTTPS` est limitée au profil Private : cette règle n’autorise donc pas elle-même l’accès sur le profil actif. Vérifier le réseau, la confiance du certificat et l’authentification avant un essai distant.
2. **Plusieurs vrais comptes Windows simultanés.** Les droits et conflits sont testés logiciellement ; les autres rôles ne sont pas affectés à des comptes réels dans la configuration examinée. L’essai d’exploitation avec plusieurs postes/comptes reste à faire.
3. **Essai prolongé.** Les tests couvrent les parcours, les défauts et les reprises, mais ne constituent pas un essai continu de plusieurs jours ni une mesure de performances à grande volumétrie.
4. **Première installation sous PowerShell 5.1.** `Install-WindowsService.ps1` appelle `Path.IsPathFullyQualified`, absent de ce runtime. Utiliser PowerShell 7 pour ce script. Le déploiement du service existant est opérationnel.

Aucun échec n’a été observé dans les contrôles exécutés. Cette conclusion concerne le périmètre testé en simulation ; elle ne vaut pas validation des fonctions absentes ou des essais encore non réalisés.
