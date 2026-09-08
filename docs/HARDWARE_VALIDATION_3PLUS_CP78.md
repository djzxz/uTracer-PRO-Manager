# uTracer 3+ — walidacja sprzętowa po poprawkach CP78

Status: **WYMAGANA PRZED MERGE DO WYDANIA POMIAROWEGO**

Ta procedura nie zastępuje zasad bezpieczeństwa producenta. W układzie występują napięcia niebezpieczne. Test wykonuje osoba posiadająca odpowiednie przygotowanie, przy osłoniętym stanowisku i z kontrolą rozładowania.

## Cel

Porównać uTracer PRO Manager z oryginalnym GUI V3.12.6 na tym samym egzemplarzu uTracera 3+ i tej samej kalibracji. Potwierdzić ramki protokołu, auto/ręczny PGA, uśrednianie, compliance, skorygowane Va/Vs, Ia/Is oraz bezpieczne zakończenie.

## 1. Stan wejściowy

Zapisz:

- numer/oznaczenie płytki i wersję firmware,
- napięcie zasilacza oraz jego ograniczenie prądowe,
- użyty plik kalibracji `.cal` i jego SHA-256,
- eksport ustawień `.uts` z oryginalnego GUI,
- wersję uTracer PRO Manager i identyfikator commita,
- temperaturę otoczenia i użyte mierniki.

Nie używaj do porównania profilu oznaczonego `BLOCKED`, `REQUIRES_MODIFICATION` ani wariantu NXT/6 bez zatwierdzonego adaptera.

## 2. Test bez lampy

1. Uruchom odczyt jałowy ADC.
2. Potwierdź Vsu i napięcie ujemne.
3. Sprawdź, że HEATER jest kodem 0 przed START.
4. Wykonaj punkty niskonapięciowe przewidziane przez kreator kalibracji.
5. Po END zmierz czas zaniku napięcia i porównaj z czasem raportowanym przez aplikację.

Kryterium: brak niezamówionego napięcia na wyjściach i brak różnicy kolejności komend względem zatwierdzonej sekwencji.

## 3. Obciążenia rezystorowe

Dla anody i ekranu użyj odpowiednio dobranych rezystorów mocy / sztucznego obciążenia, aby uzyskać kilka bezpiecznych punktów prądowych bez lampy.

Dla każdego punktu zapisz równolegle:

- `Va_set`, `Va_measured`,
- `Vs_set`, `Vs_measured`,
- `Ia`, `Is`,
- kod compliance,
- kod PGA anody,
- kod PGA ekranu,
- kod averaging,
- status odpowiedzi,
- czas od START do odpowiedzi,
- zachowanie po wymuszeniu limitu prądowego.

Sprawdź Auto PGA oddzielnie dla anody i ekranu oraz po jednym ręcznym zakresie. Sprzętowy próg compliance jest wspólny, natomiast programowe limity Ia/Is muszą pozostać oddzielne.

## 4. Test progu 95%

Przygotuj bezpieczny plan, którego jeden z punktów przekroczyłby 95% limitu mocy/prądu profilu. Program ma odmówić wysłania START jeszcze przed rozpoczęciem skanu.

Następnie wykonaj plan poprawny i, na kontrolowanym obciążeniu, sprawdź reakcję po osiągnięciu 95% programowego limitu na rzeczywistym `Va_measured` / `Vs_measured`.

Kryterium: preflight blokuje plan niebezpieczny; pomiar aktywny jest przerwany przy przekroczeniu limitu po korekcji napięcia.

## 5. Lampa wzorcowa

Użyj sprawnej lampy o znanych, wcześniej powtarzalnych wynikach. Wykonaj w oryginalnym GUI i w uTracer PRO Manager identyczny:

- Quick Test,
- skan anodowy z kilkoma Vg,
- pomiar Ia/Is,
- test gm/Rp/μ,
- jeden skan z Auto PGA i Auto Averaging,
- zapis i ponowne odtworzenie `.uts`.

Porównaj punkt po punkcie wartości zadane i rzeczywiste. Nie porównuj wyłącznie wartości nominalnej z karty.

## 6. +Vg

Tryb dodatniego napięcia siatki uruchamiaj wyłącznie po potwierdzeniu specjalnego okablowania. W uTracer 3+ dodatnie Vg ma być podawane przez wyjście SCREEN; standardowe wyjście GRID pozostaje przy 0 V. `Is` w tym trybie reprezentuje prąd siatki.

## 7. Zewnętrzne żarzenie

Dla profilu `READY_EXTERNAL_HEATER`:

- aplikacja ma odmówić planu bez potwierdzenia zewnętrznego zasilacza,
- kod wewnętrznego HEATER pozostaje 0,
- operator potwierdza napięcie i sposób zasilania przed START,
- informacja o zewnętrznym żarzeniu trafia do wyniku sesji.

## 8. Kryterium wydania

Nie oznaczaj wariantu jako `measurement ready`, dopóki nie ma kompletnego protokołu z tego testu. Minimalne wymagania wydania:

- wszystkie ramki START/GET/HOLD/END zgodne z oczekiwaniem,
- oddzielne zachowanie PGA Ia/Is potwierdzone,
- Auto Averaging potwierdzone,
- zgodność korekcji Va/Vs,
- zadziałanie 95% preflight i 95% runtime guard,
- poprawny Quick Test na sprzęcie,
- poprawne rozładowanie po sukcesie, błędzie i CANCEL,
- brak modyfikacji historii użytkownika i checkpointów bazy.
