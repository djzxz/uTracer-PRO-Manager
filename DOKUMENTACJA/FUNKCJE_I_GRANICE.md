# Funkcje, wersje i granice sprzętowe

Stan: 2026-08-11.

## Punkt odniesienia

| Warstwa | Potwierdzona wersja | Znaczenie |
|---|---:|---|
| uTracer PRO Manager WPF | 1.1.24 | Źródło dotychczasowego silnika pomiarów i bazy |
| uTracer PRO Manager Avalonia | 1.2.7 | Pionowe panele pomiaru, graficzny pinout, zapisywany układ paneli, 13 pomiarów V3.12.6 i baza v2.43.1 |
| baza profili pomiarowych | 2.43.1 / schemat 7 | 21 505 profili, 976 READY, 22 156 kart, 15 885 modeli i 217 producentów |
| oryginalne GUI u-Tracer | V3.12.6 | Potwierdzone bezpośrednio przez dostarczony plik `uTracer_3p12p6.exe` i jego ekran |
| uTmax GUI | 3.07a | Oddzielny, alternatywny program; nie jest numerem oryginalnego GUI |
| alternatywny firmware uTmax | 3.07 | Osobny procesor/firmware; część funkcji wymaga zmian sprzętu |
| „3.16.5” | brak potwierdzenia | Nie jest wymaganiem ani źródłem danych |

## Macierz funkcji

| Funkcja | Avalonia 1.2.7 | Następny etap / ograniczenie |
|---|---|---|
| Profile SQLite i karty producentów | działa dla schematów v2 i v7; pakiet zawiera v2.43.1 oraz dane z sześciu kompletnych paczek PDF | dalsza rozbudowa samej bazy niezależnie od programu |
| Ekran pomiaru | pionowy panel wartości/pinoutu/egzemplarza obok pionowego panelu charakterystyk; aktywny model/producent i stan kalibracji na górnym pasku | test czytelności na rozdzielczości użytkownika |
| Graficzny pinout | numerowana podstawka od spodu, klucz orientacyjny, dymki funkcji i pełny opis tekstowy; brak zgadywania przy niejednoznacznym opisie | rozbudować o osobne, zatwierdzone rysunki nietypowych podstawek |
| Układ paneli | przeciągane separatory pomiaru, bazy i panelu ręcznego; szerokości, wysokość oraz rozmiar okna są zapisywane lokalnie | test ergonomii na ekranach o innym skalowaniu DPI |
| Zapisane egzemplarze | wyszukiwanie historii po numerze, producencie, typie i kodach; odtworzenie wyniku i aktualnego profilu; nowy test zawsze tworzy nowy rekord | rozbudowa o zbiorcze porównanie wielu dat |
| Wyszukiwanie producent + model + profil | działa na żywo i przez Enter; wpis BLOCKED jest czerwony i nie może załadować profilu | testy UX na Windows |
| Wybór wersji sprzętu | pięć pozycji z macierzy SQLite; profil jest oceniany osobno dla wybranego wariantu | NXT/uTracer 6 i uTmax są na tym etapie wyborem katalogowym; protokół fizyczny pozostaje zablokowany bez właściwej implementacji |
| Ulubione | działa w warstwie usług | dodać osobny filtr „tylko ulubione” |
| Ręczne parametry jak w oryginalnym panelu | działa; import/eksport `.uts` | rozbudować widok o wszystkie osie i zakresy; nieedytowane pola są zachowywane 1:1 |
| COM, PING/ECHO, odczyt ADC | synchroniczny Win32 COM; `GetCommState`; pięć profili zgodności DTR/RTS/flow; jeden uchwyt na sesję; ESC po błędzie | konieczny test z fizycznym uTracerem 3+ i CH340 użytkownika |
| „Znajdź uTracer” | sprawdza profile COM, echo i ADC; po identyfikacji pozostawia połączenie otwarte i pokazuje wybrany profil | sprawdzić na COM3, czy PING i ADC działają bez ponownego wpinania USB |
| Odzyskanie protokołu | `ClearCommError` przed ramką, automatyczny ESC i przycisk `WYŚLIJ ESC`; uchwyt pozostaje otwarty | porównać zachowanie z `Send esc` w V3.12.6 |
| Diagnostyka startu | programowy renderer i osobny `startup_avalonia.log` | test na Windows 10 19045 użytkownika |
| Szybki / normalny A-B / pełny test | działa w silniku | konieczny test sprzętowy i porównanie z programem referencyjnym |
| gm, Rp, μ, stabilność, odstające serie | działa | porównać wyniki na lampach wzorcowych |
| Charakterystyki i wykres | działa; osobny panel referencyjny ma 13 trybów, wybór Ia/Is/gm, siatkę, zachowanie i czyszczenie krzywych | porównać każdy tryb z fizycznym V3.12.6 |
| PDF, XLSX, CSV, Quick Test TXT i wykresy PNG | działa | test wizualny raportu na Windows |
| Kalibracja z ilustracjami i suwakami | działa; widoczne także wartości starszego `.cal` | obowiązkowy test kolejnych punktów na płytce użytkownika |
| Import/eksport oryginalnej kalibracji `.cal` | działa dla pozycyjnego formatu rozszerzonego V3/V3+ | import nie oznacza automatycznie pełnej kalibracji v2; Vn wymaga pomiaru |
| Import/eksport ustawień `.uts` | działa dla dostarczonych układów V3.11 i V3.12.6 | pinning i pola bez kontrolek są zachowywane, nie są zgadywane |
| Panel oryginalnych pomiarów V3.12.6 | 13 udokumentowanych trybów, opis „co mierzy / kiedy użyć”, zakres, interwały, stałe, averaging, compliance, delay, UL i Schade | test sprzętowy ramek i porównanie wyników punkt po punkcie |
| Niezależne skany Va/Vs/Vg/Vh | działa w panelu referencyjnym | test sprzętowy wszystkich kombinacji |
| Triodowe połączenie pentody / dodatnie Vg | jawne tryby i obowiązkowe potwierdzenie specjalnego okablowania | operator musi wykonać połączenie zgodne ze schematem lampy |
| Dual triode / dopasowanie A-B | działa | rozszerzyć na dobór par i kwartetów z historii |
| Model matematyczny i SPICE | brak | wymaga poprawnego fitowania i miary jakości; nie generować pozornego modelu |
| Live LCD / podgląd podczas skanu | postęp tekstowy | dodać bezpieczny strumień próbek do GUI |
| Markery, pan, zoom | podstawowe możliwości ScottPlot | dodać opis punktu i zamrażanie markera |
| Matryca automatycznego pinoutu | brak | wymaga osobnej płytki i potwierdzonego protokołu |
| Test zwarć elektrod | brak | w uTmax opisany jako alpha i wymaga Matrix Board; nie uruchamiać bez niej |
| 12-bit Vg | zablokowane dla stock | wymaga alternatywnego firmware'u i właściwego protokołu |
| Fast capture | zablokowane dla stock | wymaga alternatywnego firmware'u |
| Kalibracja w procesorze | zablokowane dla stock | wymaga alternatywnego firmware'u |
| Aktualizacja firmware'u | zablokowane | nie implementować bez obrazu firmware'u, identyfikacji CPU i procedury odzyskania |
| Przełączany niski zakres prądowy | zablokowane | firmware 3.07 i dodatkowy układ rezystor/FET |

## Co jeszcze można uzyskać bez modyfikacji testera

Można bezpiecznie rozbudować analizę danych po stronie komputera:

- automatyczne dobieranie par i kwartetów z historii według Ia, gm, Rp i μ;
- porównanie starzenia tej samej lampy w czasie;
- definiowane przez użytkownika siatki skanu oraz ponowne przeliczanie wyników;
- dopasowanie modeli Koren/Reefman i eksport SPICE z raportem błędu dopasowania;
- wiele zsynchronizowanych wykresów, kursory, adnotacje i eksport;
- wykrywanie dryftu termicznego, szumu i słabej powtarzalności;
- automatyczne dobieranie zakresu i liczby próbek w granicach fabrycznego protokołu;
- kontrola spójności kalibracji i ostrzeganie o jej zmianie.

## Co wymaga sprzętu lub firmware'u

Według dokumentacji alternatywnego firmware'u: 12-bitowa siatka, fast capture, zapis kalibracji w procesorze i niski zakres prądowy nie są zwykłymi funkcjami interfejsu. Niektóre wymagają wymiany procesora, zmian w torze żarzenia albo dodatkowego przełączanego toru pomiarowego. Program ma je blokować, dopóki użytkownik nie wybierze dokładnie rozpoznanego wariantu sprzętu.

Źródła:

- oryginalne GUI V3.12.6: ekran oraz plik `uTracer_3p12p6.exe` dostarczone przez użytkownika;
- uTmax GUI i historia 3.07a: https://bmamps.com/v01/home/techie-corner/utracer-utmax/
- alternatywny firmware 3.07: https://bmamps.com/v01/utracer-firmware-chip/
- dokumentacja uTracera 3+: https://www.dos4ever.com/uTracer3/uTracer3.html
- instrukcja użytkownika i definicje trybów pomiarowych: https://www.dos4ever.com/uTracer3/uTracer3_user_man.pdf
- opis pomiarów i charakterystyk: https://www.dos4ever.com/uTracer3/uTracer3_pag5.html
- Avalonia: https://docs.avaloniaui.net/docs/get-started/install-avalonia
- Microsoft Win32 `SetCommState` / zachowanie DCB z `GetCommState`: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setcommstate
- Microsoft Win32 `ClearCommError` / odblokowanie I/O po `fAbortOnError`: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-clearcommerror
- oficjalny sterownik WCH CH340/CH341 dla Windows: https://www.wch-ic.com/downloads/CH341SER_ZIP.html
