# uTracer PRO Manager Avalonia v1.2.7

Scalone wydanie programu i bazy pomiarowej. Wersja 1.2.7 przywraca pionowe panele egzemplarza i charakterystyk, dodaje na stronie głównej numerowany pinout widziany od spodu, zapamiętuje ustawione przez użytkownika szerokości i wysokości paneli oraz zawiera bazę v2.44.0. Program WPF v1.1.24 pozostaje osobnym, starszym wydaniem.

## Ważna korekta numerów wersji

Zdjęcie i nazwa pliku dostarczone przez użytkownika jednoznacznie potwierdzają oryginalne GUI **u-Tracer V3.12.6** (`uTracer_3p12p6.exe`). Wersja **uTmax 3.07a** to oddzielny program alternatywny i nie wolno jej przedstawiać jako wersji oryginalnego GUI. Nadal nie ma wiarygodnego potwierdzenia numeru **3.16.5**.

Zweryfikowane oznaczenia, sprawdzone 2026-08-11:

- nasz dotychczasowy uTracer PRO Manager: **1.1.24**;
- oryginalne GUI pokazane przez użytkownika: **V3.12.6**;
- oddzielne alternatywne GUI uTmax: **3.07a**;
- opisany alternatywny firmware uTmax: **3.07**;
- fabryczny uTracer 3+ jest oddzielnym wariantem sprzętu/firmware'u i pozostaje domyślnym trybem bezpiecznym.

Źródła:

- https://bmamps.com/v01/home/techie-corner/utracer-utmax/
- https://bmamps.com/v01/utracer-firmware-chip/
- https://www.dos4ever.com/uTracer3/uTracer3.html

## Co działa w tym wydaniu

- Avalonia UI na .NET 8, układ dopasowywany do obszaru roboczego ekranu;
- oddzielone projekty: rdzeń pomiarowy, infrastruktura i GUI;
- wymienna baza SQLite v2.44.0 w pełnym schemacie v7; stare eksporty JSON/CSV v1.19 nie są już źródłem danych programu;
- 21 505 profili katalogowych, w tym 879 profili READY po ponownym audycie; 22 156 kart, 15 885 modeli i 217 producentów;
- 640 dokładnych kart producent–model dopuszczonych do załadowania profilu; pozostałe wpisy pozostają widoczne i zablokowane;
- wszystkie 976 dotychczasowe profile READY sprawdzono ponownie; 97 cofnięto do blokady, ponieważ punkt odniesienia nie zachowywał wymaganego 5% zapasu względem udokumentowanej mocy strat;
- wszystkie 9 662 pozycje manifestów z sześciu paczek mają zapisany wynik ponownego audytu; automatyczne OCR nie promuje profilu do READY;
- 99 nowych, unikalnych wariantów producent–typ w tej partii (łącznie 574 w v2.25+v2.26+v2.27), oznaczonych `PASUJE DO`; wartości wykonawcze są kopiami 1:1 istniejących rekomendacji READY, tożsamości i treści docelowych kart nie sprawdzano ponownie, ocena procentowa jest wyłączona, a potwierdzenie operatora obowiązkowe;
- ekran pomiaru ma dwa pionowe panele obok siebie: po lewej wartości, pinout i mierzony egzemplarz, po prawej charakterystyki i wynik;
- pinout na stronie głównej pokazuje ponumerowaną podstawkę widzianą od spodu, klucz orientacyjny, funkcje pinów w dymkach oraz pełny opis tekstowy z profilu; gdy opis jest niejednoznaczny, program nie zgaduje numeracji;
- szare separatory pozwalają przeciągać szerokość panelu pomiarowego, wysokość wartości/pinoutu, szerokość bazy lamp oraz panelu ręcznego; ustawienia są zapisywane w `%LOCALAPPDATA%\uTracerProManagerAvalonia\layout.json`;
- baza lamp pozostaje poziomym zestawieniem profili oraz oryginalnych kart producentów, z regulowaną szerokością obu części;
- górny pasek pokazuje tylko model i producenta aktywnej lampy, krótki wariant sprzętu, datę/stan kalibracji i stan połączenia;
- zapisane pomiary można wyszukiwać po numerze, producencie, typie i kodach; wybór odtwarza wynik, dane egzemplarza i aktualny dopuszczony profil, a `NOWY EGZEMPLARZ` przygotowuje osobny rekord;
- pola edycyjne są około 25% niższe, a ich tekst większy;
- wyszukiwanie profilu, producenta i oryginalnej karty; dokładne powiązanie karta–producent–model–profil z tabeli rekomendacji schematu v7;
- wyszukiwanie uruchamia się podczas pisania oraz klawiszem Enter; profil zablokowany dla wybranego sprzętu jest czerwony i nie można go załadować;
- osobne wskazanie profilu na liście i jawny przycisk `ZAŁADUJ PROFIL`, dzięki czemu samo zaznaczenie nie podaje napięć na tester;
- wybór pięciu wariantów sprzętu: uTracer 3+ stock, modyfikacja 600 mA, uTmax 3.07, uTracer NXT i uTracer 6; niezgodny wariant pozostaje zablokowany;
- profile ulubione i profile ręczne; niepotwierdzony profil ręczny pozostaje zablokowany;
- jawne połączenie COM, PING/ECHO i jawne wyszukiwanie testera;
- natywne, synchroniczne otwieranie COM jak w starszych programach Windows; konfiguracja zaczyna się od `GetCommState`, zachowuje poprawne pola sterownika i ustawia tylko wymagane 9600 8N1;
- automatyczne sprawdzenie pięciu bezpiecznych wariantów DTR/RTS i kontroli przepływu; ostatni wariant potrafi użyć już poprawnego 9600 8N1 bez wywołania odrzucanego przez CH340 `SetCommState`;
- `ClearCommError` przed każdą ramką usuwa stan błędu sterownika, który w trybie `fAbortOnError` blokuje dalszy odczyt i zapis aż do jawnego skasowania błędu;
- każda próba kończy się bezpiecznym ESC + END/PING; wybrany wariant połączenia jest widoczny w nagłówku, a błąd podaje dokładny etap Win32 (`CreateFile`, `GetCommState`, `SetCommState`, timeout lub echo);
- jedno otwarte połączenie na całą sesję: po „Znajdź uTracer” port pozostaje otwarty i następne polecenie używa tego samego uchwytu;
- automatyczne wysłanie ESC po przerwanym poleceniu oraz jawny przycisk `WYŚLIJ ESC`, bez zamykania portu;
- emulator wyłącznie po zaznaczeniu przez operatora, z trwałym oznaczeniem wyniku;
- szybki test, normalny test A/B i pełna diagnostyka z istniejącego silnika v1.1.24;
- rampowanie żarzenia, stabilizacja, odrzucanie odstających serii, gm, Rp, μ, dopasowanie sekcji A/B i skan charakterystyk;
- wykres rzeczywistych próbek albo charakterystyk po zakończeniu testu;
- referencyjny panel krzywych z wszystkimi 13 trybami V3.12.6: skany Va/Vs/Vg/Vh, tryby z dodatnim Vg, połączenia Va=Vs, ultralinear i Schade feedback; każdy wybór ma osobny opis „co mierzy” i „kiedy użyć”, a panel zachowuje stałe, zakres, kroki, averaging, compliance, delay, potwierdzenie specjalnego okablowania oraz wybór osi Y;
- formuły ultralinear i Schade feedback są jawne, a każdy rzeczywisty skan przechodzi przez kalibrację, limity napięcia, prądu i mocy oraz bezpieczne ESC/END;
- historia pomiarów i eksport PDF/XLSX/CSV/PNG;
- eksport raportu Quick Test `.txt` w układzie oryginalnego GUI;
- kreator kalibracji v2 z ilustracjami, punktami pomiarowymi, suwakami oraz sprzętowymi poleceniami HOLD/STOP;
- natywny import i eksport pozycyjnych plików kalibracji `.cal` zgodnych z GUI V3.11/V3.12.6;
- import i eksport 147-liniowych ustawień `.uts`; nieedytowane pola osi, zakresów, pinningu i krzywych są zachowywane przy ponownym eksporcie;
- widoczne wartości Vgrid low/4 V/40 V, Vsat, Vglow/spare, offset i slope oraz szersze pola liczb ze znakiem `+`/`-`;
- atomowa podmiana bazy z kopią w `BACKUP_BAZY`; przy starcie nowsza baza z paczki automatycznie zastępuje starszą bazę użytkownika;
- log otwierany ze współdzieleniem, aby nie powtarzać błędu „file is being used by another process”;
- diagnostyka startu Avalonia, czytelne okno błędu oraz log `%LOCALAPPDATA%\uTracerProManager\Logs\startup_avalonia.log`;
- programowy renderer Avalonia na Windows, aby ograniczyć awarie sterownika grafiki przy starcie;
- funkcje alternatywnego firmware'u są rozpoznawane jako osobny wariant i blokowane bez jawnego potwierdzenia modyfikacji.

## Czego to wydanie jeszcze nie deklaruje

- Nie wykonano testu z fizycznym uTracerem 3+. Przed podłączeniem lampy należy sprawdzić PING, odczyt ADC i pełną kalibrację bez lampy.
- Panel krzywych odwzorowuje 13 udokumentowanych trybów pomiarowych V3.12.6, lecz nie jest kopią jego kodu i wymaga porównania ramek oraz wyników na fizycznym testerze. Nieedytowane dane `.uts` są nadal zachowywane 1:1.
- Nie ma jeszcze pełnej zgodności funkcjonalnej z uTmax 3.07a. Brakuje przede wszystkim dopracowanego edytora niezależnych skanów Va/Vs/Vg, dopasowywania modeli matematycznych i eksportu SPICE, wielu zakładek wykresów z wyborem osi, live-view oraz obsługi płytki matrycy pinów.
- Fast capture, 12-bit Vg, kalibracja zapisywana w procesorze, aktualizacja firmware'u i przełączany niski zakres prądowy nie są implementowane jako aktywne polecenia. Wymagają alternatywnego procesora/firmware'u i w części także zmian sprzętowych. Samo GUI nie może ich bezpiecznie odblokować.
- Transport sprzętowy w tym pakiecie jest obecnie implementacją Windows x64. Warstwa GUI jest przenośna, ale transport Linux wymaga osobnego testu.

Szczegółowa macierz znajduje się w `DOKUMENTACJA/FUNKCJE_I_GRANICE.md`.

## Uruchomienie

1. Rozpakuj cały ZIP do nowego folderu.
2. Zamknij oryginalny `uTracer_3p12p6.exe`, WPF Manager i każdy terminal COM — w danej chwili tylko jeden program może posiadać COM3.
3. Uruchom `uTracerProManager.Avalonia.exe` tylko z tego rozpakowanego folderu.
4. Baza v2.44.0 jest kopiowana do `%LOCALAPPDATA%\uTracerProManagerAvalonia\Data\tube_measurements.db`. Jeżeli znajduje się tam starsza baza, program zachowa jej kopię w `Data\BACKUP_BAZY` i bezpiecznie zainstaluje nowszą.
5. Wybierz fabryczny uTracer 3+, port COM i kliknij `ZNAJDŹ uTRACER`. Program sam sprawdzi zgodne warianty sterownika; po wykryciu status musi pozostać `POŁĄCZONY` i pokazać użyty profil COM.
6. Wykonaj kolejno PING, odczyt ADC i kreator kalibracji bez lampy. Jeśli polecenie zostanie przerwane, użyj `WYŚLIJ ESC` zamiast odłączać USB.
7. Nie wybieraj wariantu uTmax 3.07, jeżeli tester nie ma właściwego alternatywnego procesora i wymaganych modyfikacji.

## Budowanie

Wymagany jest .NET SDK 8.0. Projekt używa Avalonia 11.3.18, ponieważ ten etap przebudowy jest budowany i publikowany na istniejącym toolchainie .NET 8.

```text
dotnet restore src/uTracerProManager.Avalonia/uTracerProManager.Avalonia.csproj
dotnet build src/uTracerProManager.Avalonia/uTracerProManager.Avalonia.csproj -c Release
dotnet run --project tests/uTracerProManager.SelfTest/uTracerProManager.SelfTest.csproj -c Release
dotnet publish src/uTracerProManager.Avalonia/uTracerProManager.Avalonia.csproj -c Release -r win-x64 --self-contained true
```

## Wynik self-testu tego pakietu

```text
uTracer PRO Manager Avalonia v1.2.7 — self-test
DATABASE: 2.44.0; 21505 profiles; 879 ready
SELFTEST PASSED
```

Self-test nie zastępuje pomiaru kontrolnego z fizycznym testerem.
