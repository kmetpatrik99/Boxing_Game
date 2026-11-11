# Razlaga skript – Boxing Game

Ta dokument je kratek zemljevid kode. V glavnih igralnih skriptah so dodani tudi komentarji pred funkcijami; tukaj je razloženo, kaj posamezna datoteka upravlja.

## Assets/Scripts/Boxing/

### Fighter.cs – borec in bojna mehanika
To je osrednja skripta posameznega borca (igralec in nasprotnik uporabljata isto skripto).
- `PunchData` hrani nastavitve udarca: škodo, doseg, porabo vzdržljivosti, čas zadetka in omamitev.
- `Awake` poišče animator in doda zvočne učinke, če manjkajo.
- `Update` posodablja gibanje, obračanje, ločevanje borcev in meje arene.
- `TryPunch` / `PunchRoutine` sprožita udarec in določita trenutek, ko lahko zadene.
- `TryDodge` / `DodgeRoutine` izvedeta izmik, porabo vzdržljivosti in kratko obdobje neranljivosti.
- `ReceiveHit` obdela zadetek, blokado, škodo in povratni sunek.
- `ResetFighter` pripravi borca za novo rundo.

### PlayerController.cs – kontrole igralca
Bere tipkovnico, miško in igralni plošček prek novega Unity Input Systema. Premikanje pošilja v `Fighter`, udarce začasno shrani v vhodni medpomnilnik, izmik pa sproži z izbrano smerjo. Tukaj se spreminjajo tipke in način vnosa.

### BoxerAI.cs – računalniški nasprotnik
Odloča, kdaj se nasprotnik približa, kroži, umakne, blokira ali napade. `difficulty` določa hitrost odziva in pogostost napadov. `Combos` vsebuje zaporedja udarcev, `Think` sprejme odločitev, `RunCombo` izvede kombinacijo.

### BoxingCamera.cs – kamera
V `LateUpdate` spremlja oba borca, izračuna razdaljo in usmeri pogled mednju. Dogodek `Fighter.HitImpact` sproži kratko tresenje kamere.

### BoxingAudioEffects.cs – zvočni učinki
Zvoki se ustvarijo programsko, zato projekt ne potrebuje dodatnih zvočnih datotek. `CreateClips` pripravi zvoke; `MakeImpact` ustvari udarce in korake; `MakeWhoosh` ustvari zračni zvok izmika; `MakeBell` ustvari mehkejši kovinski zvonec. `PlayPunch`, `PlayDash`, `PlayBell` in `UpdateMovementSound` jih predvajajo.

### GameManager.cs – upravljanje tekme in uporabniški vmesnik
Upravlja menije, težavnost, izbiro arene, odštevanje, runde, čas, rezultat, konec tekme in HUD. `StartMatch` začne tekmo, `StartRound` pripravi rundo, `BeginFight` začne boj, `EndRound` zaključi rundo. Metode `Draw...` rišejo elemente zaslona; `HandleMenuInput` in `HandleMenuMouse` skrbita za izbiro v meniju.

## Assets/Editor/BoxingGameBuilder.cs – izdelava scene
To je Unity Editor orodje, ki ustvari oziroma obnovi igralno sceno: arene, kletko, luči, borca, kamero, animatorje in povezave komponent. Če spremeniš gradnjo scene, spremembe naredi tukaj in nato v Unityju zaženi ustrezen ukaz iz menija Boxing. To ni skripta, ki bi se izvajala kot običajna igralna logika med dvobojem.

## Assets/TutorialInfo/Scripts/
`Readme.cs` in `ReadmeEditor.cs` sta Unityjevi predstavitveni/tutorial skripti. Nista del boksarske mehanike; lahko ju pustiš pri miru.

## Opomba za zvoke
Zvoki so proceduralni in se ustvarijo ob zagonu igre. Glasnost zvonca je namenoma znižana. Če želiš glasnejše/tišje zvoke, prilagodi vrednosti v `BoxingAudioEffects.cs` pri `PlayBell`, `PlayPunch` ali `PlayDash`.
