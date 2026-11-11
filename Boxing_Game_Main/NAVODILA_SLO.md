# Boxing Game – Unity 6.1.5f1

Ta projekt temelji na izvirnem projektu. Ohranjen je izvirni Unity Editor version:
**6000.1.5f1**.

## Dodani zvoki
- zvonec ob začetku vsake runde,
- ločen zvok za Jab, Cross in Uppercut,
- zvok korakov med hojo,
- zvok Dash.

Ni glasbe v ozadju, zvoka publike ali drugih ambientalnih zvokov.
Zvočni učinki se ustvarijo programsko, zato ni treba uvažati zvočnih datotek.

## Dash animacija
Če animator vsebuje stanje `Dash` oziroma `Dash-Back`, ga igra uporabi.
Če ga nima, se uporabi obstoječa animacija hoje s povečano hitrostjo, da Dash še vedno dobi vizualni odziv.

## Odprtje projekta
1. Razširi ZIP.
2. V Unity Hub izberi **Add > Add project from disk**.
3. Izberi mapo `Boxing_Game_Unity_6_1_Updated`.
4. Odpri z Unity **6000.1.5f1**.
5. Odpri sceno `Assets/BOX_add.unity` in pritisni Play.

Če projekt uporablja meni **Boxing > Build Game Scene**, ga lahko uporabiš za ponovno izdelavo igralne scene.

## Kaj počnejo glavne skripte
- `PlayerController.cs`: bere igralčeve tipke in vhodne ukaze.
- `Fighter.cs`: premikanje, udarci, blokiranje, izmikanje, življenje in animacije.
- `BoxerAI.cs`: odločitve in vedenje računalniškega nasprotnika.
- `GameManager.cs`: potek tekme, runde, čas in rezultat; sproži tudi začetni zvonec.
- `BoxingAudioEffects.cs`: ustvari in predvaja zvoke udarcev, hoje, dash-a in zvonca.
- `BoxingCamera.cs`: sledenje borcema s kamero.

## Pomembno
Projekt je pripravljen za Unity 6000.1.5f1, vendar ga je treba po odprtju preveriti v Unity Editorju in zagnati Play Mode. V tem okolju Unity Editor ni bil na voljo za dejansko kompilacijo in preizkus.
