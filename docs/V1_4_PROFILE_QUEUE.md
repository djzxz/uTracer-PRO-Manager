# uTracer PRO Manager v1.4.0 — kolejka profili BLOCKED / PENDING

## Cel

Kolejka porządkuje istniejące profile, które nie są jeszcze bezpiecznie zatwierdzone do pomiaru. Nie nadaje READY automatycznie i nie wymyśla brakujących danych.

## Zasady

- każdy profil z `approved_for_hardware=0` jest widoczny w kolejce dla wybranego wariantu sprzętu;
- zapisywany szkic z edytora v1.3.1 jest uwzględniany natychmiast, ale aktywny profil nadal pozostaje BLOCKED;
- status `GOTOWY DO ZATWIERDZENIA` oznacza wyłącznie, że ten sam `ProfileReadyValidator` nie widzi już błędów; właściwa promocja następuje dopiero po ręcznym `ZWALIDUJ I ZATWIERDŹ READY`;
- źródło/PDF, pinout, żarzenie, punkt pracy, limity, krzywe, zgodność sprzętowa i 95% power guard są raportowane oddzielnie;
- profile z naruszeniem 95% limitu mocy otrzymują osobną kategorię `BLOKADA MOCY 95%`;
- kolejka wykorzystuje wcześniejsze checkpointy, `profile_manual_workspace_v268`, `profile_verification_queue`, `profile_source_verification` oraz `profile_edit_draft_v131`.

## Kategorie

1. `GOTOWY DO ZATWIERDZENIA`
2. `ŹRÓDŁO / PDF`
3. `BRAK PINOUT`
4. `BRAK ŻARZENIA`
5. `BRAK PUNKTU PRACY`
6. `BRAK LIMITÓW`
7. `BRAK KRZYWEJ`
8. `SPRZĘT DO WERYFIKACJI`
9. `BLOKADA MOCY 95%`

Kolumna `Wszystkie braki` może zawierać kilka pozycji jednocześnie; kategoria główna wskazuje następny zalecany krok.

## Przepływ pracy

1. Otwórz `KOLEJKA PROFILI`.
2. Wybierz kategorię i profil.
3. Kliknij `OTWÓRZ W EDYTORZE`.
4. Uzupełnij wyłącznie wartości potwierdzone w dokumentacji; niepewne pozostaw puste.
5. `ZAPISZ SZKIC` — aktualizuje kolejkę, ale nie zmienia statusu READY.
6. Po uzupełnieniu wszystkich danych i potwierdzeń profil pojawi się jako `GOTOWY DO ZATWIERDZENIA`.
7. `ZWALIDUJ I ZATWIERDŹ READY` zapisuje decyzję transakcyjnie wraz z audytem i historią.

## Bezpieczeństwo

Kolejka jest narzędziem organizacyjnym. Nie omija preflightu pomiarowego, limitów mocy 95%, kalibracji, zgodności sprzętowej ani wymagań dotyczących zewnętrznego żarzenia. NXT i uTracer6 pozostają bez realnego pomiaru do czasu fizycznej walidacji ich adapterów protokołu.
