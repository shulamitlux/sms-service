# SPEC – שירות שליחת SMS לעובדים

> המסמך הזה הוא האפיון המלא שלפיו נבנה הפרויקט בעזרת Claude Code.
> הוא מגדיר מה בונים, באילו טכנולוגיות, מהן הנחות היסוד ומה במפורש לא בונים.

---

## 0. הנחיות לסוכן ה-AI (Claude)

- בנה את הפרויקט **בדיוק לפי המסמך הזה**. אם משהו לא מוגדר, בחר בפתרון הפשוט והסביר ביותר ותעד את ההחלטה ב-`README.md` תחת "החלטות".
- **מסגרת זמן: 3–4 שעות עבודה.** עדיף קוד קטן, נקי ומובן על פני הרבה פיצ'רים. אל תוסיף שכבות, ספריות או תבניות שהמסמך לא דורש.
- הקוד צריך להיות כזה שהמפתח יוכל להסביר ולשנות בזמן אמת בראיון: שמות ברורים, בלי "קסמים", והערות רק במקומות שבהם יש החלטה לא מובנת מאליה.
- עבוד לפי סדר השלבים שבסעיף 16. בסוף כל שלב ודא שהפרויקט עובר build ושהבדיקות עוברות.
- אל תמציא את ה-API של ספק ה-SMS. כל הקוד שתלוי בספק נמצא במחלקה אחת (`SmsProviderClient`), כך שהתאמה לספק האמיתי תיגע במקום אחד בלבד (ראה סעיף 11).
 - עבוד עם Git לאורך כל הפיתוח, ובסיום העלה את הפרויקט ל-repository ב-GitHub (ראה סעיף 19).

---

## 1. מטרה ורקע

שירות HTTP לשליחת הודעות SMS לעובדי הארגון. השליחה בפועל נעשית מול ספק SMS מדומה שהמעסיק מספק (שרת קטן עם POST/GET).
השירות שומר כל הודעה במסד נתונים, מנהל את הסטטוס שלה ומאפשר לשאול על הסטטוס.

### נתוני העובדים (מהדרישות)
- אין לעובד מספר עובד (עקב מגבלות אבטחה).
- הזיהוי הייחודי של עובד הוא השילוב **שם פרטי + שם משפחה + כתובת**.
- לכל עובד יכולים להיות **כמה מופעים עם מספרי טלפון שונים**.
- **מספר טלפון אינו ייחודי**: אותו מספר יכול להופיע אצל כמה עובדים (למשל טלפון משותף).

---

## 2. טכנולוגיות וגרסאות

| רכיב | טכנולוגיה | גרסה |
|---|---|---|
| Backend | ASP.NET Core Web API (C#) | **.NET 10 LTS** |
| ORM | Entity Framework Core | **EF Core 10** |
| מסד נתונים | SQL Server (Developer Edition בתוך Docker) | **SQL Server 2025** (`mcr.microsoft.com/mssql/server:2025-latest`) |
| Frontend | Angular (standalone components, signals) | **הגרסה היציבה העדכנית ביותר שנמצאת בתמיכה פעילה** |
| Runtime ל-Frontend | Node.js | **Node.js 24 LTS** (או גרסת ה-LTS הזוגית העדכנית יותר, אם Angular תומכת בה) |
| בדיקות Backend | xUnit + FluentAssertions + NSubstitute | עדכני |
| בדיקות Frontend | לא נדרש (ראה סעיף 4) | – |
| הרצה | Docker Compose | – |

> **לפני תחילת העבודה, ודא את הגרסאות המותקנות בפועל:**
> `dotnet --list-sdks`, `node -v`, `ng version`.
> הגרסאות שנבחרו מתועדות ב-`global.json` (SDK של .NET), ב-`.nvmrc` (Node) וב-`package.json` (Angular).

ספריות Backend נוספות (רק אלה):
- `Microsoft.EntityFrameworkCore.SqlServer`
- `Microsoft.Extensions.Http.Resilience` (retry ו-timeout מול הספק)
- `libphonenumber-csharp` (ולידציה ונרמול של מספרי טלפון)
- OpenAPI / Swagger UI לתיעוד ה-API

---

## 3. הנחות יסוד

אלה ההנחות שהתקבלו בגלל מידע חסר במשימה. יש להעתיק אותן ל-README.

1. **השדה `to` הוא מספר טלפון**, ולא מזהה עובד (בהתאם לדרישת הוולידציה על "מספר טלפון"). המספר חייב להיות שייך לעובד אחד לפחות, אחרת `400`. מספר שמשותף לכמה עובדים מקבל הודעה אחת, והמערכת מציגה את כל העובדים שלהם הוא שייך.
2. **רק מספרי סלולר ישראליים.** הקלט מתקבל בפורמטים `050-1234567`, `0501234567`, `+972501234567` ו-`972501234567`, ונשמר בפורמט E.164 (`+972501234567`).
3. **אורך ההודעה: 1–670 תווים**, אחרי Trim. זה שווה ל-10 מקטעי SMS ב-Unicode (67 תווים במקטע, כי עברית מחייבת Unicode). המערכת מחשבת ושומרת את מספר המקטעים לצורך מידע ותמחור.
4. **השליחה אסינכרונית.** `POST /messages` שומר את ההודעה בסטטוס `Pending` ומחזיר מזהה מיד (`202 Accepted`), ותהליך רקע שולח אותה לספק. כך נפילה או איטיות של הספק לא מפילות את ה-API, וגם קורס באמצע לא מאבד הודעות.
5. **מזהה ההודעה הוא GUID** שנוצר בשרת, ולא מספר רץ, כדי שלא יהיה ניתן לנחש מזהים.
6. **נתוני העובדים נטענים מ-seed** בעליית המערכת. אין API לניהול עובדים, כי זה לא נדרש.
7. **ה-seed מגיע במבנה של "מופעים"**, כלומר שורה לכל צירוף עובד+טלפון. בטעינה מאחדים את המופעים לעובד אחד עם כמה טלפונים.
8. **אין אימות משתמשים ואין Deploy**, בהתאם למשימה.
9. **רק מופע אחד של ה-API רץ**, ולכן worker רקע אחד מספיק. ההתמודדות עם כמה מופעים מתוארת במסמך חלק ב'.

---

## 4. היקף

### בתוך ההיקף
- שלושת ה-endpoints של ההודעות (סעיף 8).
- ולידציה של מספר הטלפון ושל אורך ההודעה.
- שמירה ב-SQL Server בעזרת EF Core migrations.
- Worker רקע ששולח לספק, עם retry מוגבל.
- תמיכה בסיסית ב-Idempotency (`Idempotency-Key` header) כדי למנוע שליחה כפולה בלחיצה כפולה.
- בדיקות אוטומטיות ללוגיקה הקריטית (סעיף 13).
- ממשק Angular פשוט: טופס שליחה וטבלת הודעות עם סינון לפי סטטוס.
- הרצה מקומית בפקודה אחת: `docker compose up --build`.

### מחוץ להיקף (במכוון)
- אימות והרשאות.
- ספק SMS נוסף, ניתוב בין ספקים, קמפיינים ותורים חיצוניים (RabbitMQ/Kafka). נדונים בחלק ב'.
- API לניהול עובדים (CRUD).
- בדיקות Frontend ו-E2E, כי הממשק אופציונלי ואינו חלק מהציון.
- Pagination מלא. יש רק הגבלה של עד 100 תוצאות, הכי חדשות קודם.
- ניטור, מטריקות ו-dashboards. יש רק לוגים מובנים.

---

## 5. ארכיטקטורה

```
 Angular (nginx :4200)
        │  HTTP/JSON
        ▼
 ASP.NET Core API (:8080) ──── EF Core ────► SQL Server 2025 (:1433)
        │                                      ▲
        │  BackgroundService (SendWorker) ─────┘  (קורא Pending, מעדכן סטטוס)
        ▼
 SmsProviderClient ──HTTP──► ספק SMS מדומה (מסופק ע"י המעסיק)
```

### מבנה תיקיות

```
/
├── docker-compose.yml
├── README.md
├── SPEC.md
├── global.json
├── .nvmrc
├── src/
│   ├── SmsService.Api/
│   │   ├── Program.cs                 # DI, endpoints, migrations ו-seed בעלייה
│   │   ├── Endpoints/MessagesEndpoints.cs
│   │   ├── Domain/
│   │   │   ├── Message.cs             # ישות ומעברי סטטוס
│   │   │   ├── MessageStatus.cs
│   │   │   ├── Employee.cs
│   │   │   └── EmployeePhone.cs
│   │   ├── Validation/
│   │   │   ├── PhoneNumberNormalizer.cs
│   │   │   └── MessageBodyRules.cs    # אורך וחישוב מקטעים
│   │   ├── Data/
│   │   │   ├── AppDbContext.cs
│   │   │   ├── Migrations/
│   │   │   └── EmployeeSeeder.cs      # איחוד מופעים לעובדים
│   │   ├── Sending/
│   │   │   ├── SendWorker.cs          # BackgroundService
│   │   │   ├── ISmsProviderClient.cs
│   │   │   └── SmsProviderClient.cs   # הקוד היחיד שמכיר את ה-API של הספק
│   │   ├── Contracts/                 # DTOs של בקשה ותגובה
│   │   └── Dockerfile
│   └── sms-web/                       # Angular
│       ├── src/app/...
│       ├── nginx.conf
│       └── Dockerfile
└── tests/
    └── SmsService.Tests/
```

**החלטה:** פרויקט API אחד עם Minimal APIs ותיקיות לפי אחריות, ולא Clean Architecture עם 4 פרויקטים. בהיקף כזה ריבוי שכבות מוסיף טקס בלי ערך. ההפרדה החשובה היחידה היא בין הלוגיקה לבין הספק (`ISmsProviderClient`), כי שם צפוי שינוי (ספק שני).

---

## 6. מודל נתונים

### `Employees`
| שדה | טיפוס | הערות |
|---|---|---|
| `Id` | `int` IDENTITY, PK | מפתח פנימי (surrogate). לא נחשף כ"מספר עובד" |
| `FirstName` | `nvarchar(100)` NOT NULL | |
| `LastName` | `nvarchar(100)` NOT NULL | |
| `Address` | `nvarchar(300)` NOT NULL | |

- **אינדקס ייחודי:** `(FirstName, LastName, Address)`, שהוא המפתח הטבעי מהדרישות.
- לפני שמירה ולפני השוואה עושים Trim לשדות ומכווצים רווחים כפולים, כדי ש-"ישראל  כהן" ו-"ישראל כהן" לא ייחשבו לשני עובדים.

### `EmployeePhones`
| שדה | טיפוס | הערות |
|---|---|---|
| `Id` | `int` IDENTITY, PK | |
| `EmployeeId` | `int` FK → Employees | |
| `PhoneE164` | `varchar(16)` NOT NULL | מנורמל |

- **אינדקס ייחודי:** `(EmployeeId, PhoneE164)`, כדי שלא יהיה אותו טלפון פעמיים לאותו עובד.
- **אינדקס לא ייחודי:** `PhoneE164`. הטלפון אינו ייחודי בין עובדים, והאינדקס משמש לחיפוש מהיר "של מי המספר".
- **החלטה:** טבלה נפרדת לטלפונים במקום לשמור את המופעים כשורות כפולות של עובד. כך אין כפילות של פרטי העובד, והאילוץ הייחודי על העובד נשמר.

### `Messages`
| שדה | טיפוס | הערות |
|---|---|---|
| `Id` | `uniqueidentifier`, PK | נוצר בשרת |
| `To` | `varchar(16)` NOT NULL | E.164 |
| `Body` | `nvarchar(670)` NOT NULL | |
| `Segments` | `int` NOT NULL | מספר מקטעים מחושב |
| `Status` | `varchar(20)` NOT NULL | ראה סעיף 7. נשמר כמחרוזת לקריאות ב-DB |
| `ProviderMessageId` | `nvarchar(100)` NULL | המזהה שהספק החזיר |
| `Attempts` | `int` NOT NULL DEFAULT 0 | |
| `LastError` | `nvarchar(500)` NULL | |
| `IdempotencyKey` | `nvarchar(100)` NULL | |
| `CreatedAt` | `datetimeoffset` NOT NULL | |
| `UpdatedAt` | `datetimeoffset` NOT NULL | |
| `NextAttemptAt` | `datetimeoffset` NULL | מתי לנסות שוב |
| `RowVersion` | `rowversion` | Optimistic concurrency |

אינדקסים:
- `(Status, NextAttemptAt)`, ל-worker.
- `(Status, CreatedAt DESC)`, לרשימה המסוננת.
- **ייחודי מסונן:** `IdempotencyKey WHERE IdempotencyKey IS NOT NULL`.

---

## 7. מחזור החיים של הודעה (סטטוסים)

```
Pending ──► Sending ──► Sent
   ▲           │
   │           ├──► (שגיאה זמנית, Attempts < 3) ──► Pending  (עם NextAttemptAt עתידי)
   │           │
   │           └──► (שגיאה קבועה, או Attempts == 3) ──► Failed
   │
   └── (בעלייה: Sending שתקוע יותר מ-2 דקות) ──► Pending
```

| סטטוס | משמעות |
|---|---|
| `Pending` | נשמרה וממתינה לשליחה (או לניסיון חוזר) |
| `Sending` | ה-worker לקח אותה ושולח כרגע |
| `Sent` | הספק אישר קבלה |
| `Failed` | נכשלה סופית |
| `Delivered` / `Undelivered` | **אופציונלי:** רק אם ה-GET של הספק מחזיר סטטוס מסירה. אם אין זמן, לא מממשים ומתעדים ב-README |

כללים:
- מעברי הסטטוס מוגדרים **רק** במתודות של `Message` (`MarkSending()`, `MarkSent(providerId)`, `MarkFailedAttempt(error, isTransient, now)`). מעבר לא חוקי זורק `InvalidOperationException`. זו הלוגיקה הקריטית שנבדקת.
- **שגיאה זמנית:** timeout, שגיאת רשת, HTTP 5xx או 429. **שגיאה קבועה:** HTTP 4xx (מלבד 429).
- **Backoff:** ניסיון 2 אחרי 10 שניות וניסיון 3 אחרי 30 שניות.

---

## 8. API

Base URL: `http://localhost:8080`. כל השגיאות מוחזרות בפורמט `ProblemDetails` (RFC 9457).

### `POST /messages`
Headers (אופציונלי): `Idempotency-Key: <string עד 100 תווים>`

Request:
```json
{ "to": "050-1234567", "body": "שלום, הישיבה נדחתה למחר" }
```

Response `202 Accepted`, עם Header `Location: /messages/{id}`:
```json
{ "id": "3f2c...-...", "status": "Pending" }
```

- `400 Bad Request` עם `ValidationProblemDetails` כשהקלט לא תקין (סעיף 9).
- **Idempotency:** אם `Idempotency-Key` כבר קיים, מחזירים `200 OK` עם אותו `id`, בלי ליצור הודעה חדשה. אם המפתח קיים אבל עם `to` או `body` שונים, מחזירים `409 Conflict`.

### `GET /messages/{id}`
מחזיר את **סטטוס** ההודעה בלבד, יחד עם השדות שמסבירים אותו. נמען ותוכן לא מוחזרים כאן, כי הדרישה היא סטטוס, ואין סיבה לחשוף את פרטי ההודעה לכל מי שמחזיק במזהה.

Response `200 OK`:
```json
{
  "id": "3f2c...",
  "status": "Sent",
  "attempts": 1,
  "lastError": null,
  "updatedAt": "2026-11-05T10:00:02+02:00"
}
```
- `404 Not Found` אם ההודעה לא קיימת. `400` אם ה-id אינו GUID תקין.

### `GET /messages?status=Sent`
- `status` אופציונלי ולא תלוי רישיות. ערך שאינו סטטוס קיים מחזיר `400`.
- מחזיר עד 100 הודעות, הכי חדשות קודם, עם הפרטים המלאים (הטבלה בממשק צריכה אותם). מבנה פריט:
```json
{
  "id": "3f2c...",
  "to": "+972501234567",
  "body": "שלום, הישיבה נדחתה למחר",
  "segments": 1,
  "status": "Sent",
  "attempts": 1,
  "lastError": null,
  "createdAt": "2026-11-05T10:00:00+02:00",
  "updatedAt": "2026-11-05T10:00:02+02:00",
  "employees": [ { "firstName": "ישראל", "lastName": "כהן" } ]
}
```
- השדה `employees` מחושב ב-join על `EmployeePhones.PhoneE164`. הוא יכול להיות ריק או להכיל כמה עובדים, כי הטלפון אינו ייחודי.
- Response: `{ "items": [ ... ] }`. העטיפה באובייקט מאפשרת להוסיף pagination בעתיד בלי לשבור את החוזה.

### `GET /health`
מחזיר `200` כשה-API וה-DB זמינים.

---

## 9. ולידציה

### מספר טלפון (`PhoneNumberNormalizer`)
1. ריק או null: שגיאה "מספר טלפון הוא שדה חובה".
2. מסירים רווחים, מקפים וסוגריים.
3. מפענחים עם `libphonenumber` ואזור ברירת מחדל `IL`.
4. חייב להיות מספר תקין **מסוג Mobile** ובקידומת +972. אחרת: "מספר טלפון לא תקין. יש להזין מספר סלולרי ישראלי".
5. מחזירים פורמט E.164.
6. **שייכות לעובד:** אחרי שהפורמט תקין, בודקים ב-`EmployeePhones` שהמספר שייך לעובד אחד לפחות. אחרת: "המספר אינו שייך לאף עובד במערכת". מספר שמשותף לכמה עובדים מתקבל, ונוצרת עבורו הודעה אחת.

### גוף ההודעה (`MessageBodyRules`)
1. אחרי Trim, אם הגוף ריק: "תוכן ההודעה הוא שדה חובה".
2. אורך מעל 670 תווים: "ההודעה ארוכה מדי (מקסימום 670 תווים)".
3. **חישוב מקטעים:** אם כל התווים ב-GSM-7, עד 160 תווים זה מקטע אחד ומעבר לכך כל 153 תווים הם מקטע. אחרת (Unicode, למשל עברית), עד 70 תווים זה מקטע אחד ומעבר לכך כל 67 תווים הם מקטע.

הוולידציה נעשית בשכבת ה-endpoint, לפני שמירה. כל השגיאות מוחזרות יחד, לא רק הראשונה.

---

## 10. תהליך השליחה (`SendWorker`)

`BackgroundService` שרץ בלולאה כל שנייה (ומתעורר מיד כשנוצרת הודעה חדשה, דרך `Channel<Guid>` כאות בלבד):

1. **בעלייה:** הודעות במצב `Sending` עם `UpdatedAt` ישן מ-2 דקות חוזרות ל-`Pending`. זה כיסוי לקריסה באמצע שליחה. **ידוע ומתועד:** במקרה כזה ייתכן שהספק כבר שלח, ולכן ייתכנו כפילויות. ראה חלק ב'.
2. **שליפת עבודה:** עד 20 הודעות עם `Status = Pending` ו-`NextAttemptAt` ריק או שכבר עבר, לפי `CreatedAt`.
3. **לכל הודעה:**
   - `MarkSending()` ו-SaveChanges. ה-`RowVersion` מבטיח שאם משהו אחר כבר עדכן את ההודעה, מדלגים עליה.
   - קריאה ל-`ISmsProviderClient.SendAsync(to, body, messageId)`.
   - הצלחה: `MarkSent(providerMessageId)`.
   - כישלון: `MarkFailedAttempt(...)` לפי הסיווג שבסעיף 7.
   - SaveChanges.
4. כל הודעה מטופלת ב-`try/catch` משלה, כדי שהודעה אחת לא תעצור את כל הלולאה.
5. לוג מובנה בכל מעבר סטטוס, עם `MessageId`, `Attempt` ו-`Status`. **אין לרשום לוג של גוף ההודעה ושל מספר הטלפון המלא** (פרטיות). רושמים רק את 3 הספרות האחרונות של המספר.

**החלטה:** תור בתוך ה-DB (polling על הטבלה) ולא Message Broker. ה-DB כבר קיים, והטבלה היא מקור האמת, כך שאין מצב של "נשמר ב-DB אבל לא נכנס לתור". זה מספיק לעומס של המשימה. מעבר לתור אמיתי נדון בחלק ב'.

---

## 11. אינטגרציה עם ספק ה-SMS

- הממשק:
  ```csharp
  public interface ISmsProviderClient
  {
      Task<ProviderSendResult> SendAsync(string toE164, string body, Guid messageId, CancellationToken ct);
      Task<ProviderStatusResult?> GetStatusAsync(string providerMessageId, CancellationToken ct); // אופציונלי
  }
  ```
- המימוש `SmsProviderClient` משתמש ב-`HttpClient` רשום עם:
  - `BaseAddress` מקונפיגורציה: `SmsProvider:BaseUrl` (משתנה סביבה `SmsProvider__BaseUrl`).
  - Timeout של 5 שניות לבקשה.
  - **בלי** retry ברמת ה-HttpClient. ה-retry מנוהל ב-worker לפי `Attempts`, כדי שיהיה מקור אמת אחד שנשמר ב-DB ושורד קריסה.
- **החוזה המשוער של הספק** (יש להתאים לשרת שיסופק בפועל, ורק בקובץ הזה):
  - `POST {BaseUrl}/send` עם body `{ "to": "...", "message": "...", "reference": "<messageId>" }` מחזיר `{ "id": "..." }`.
  - `GET {BaseUrl}/status/{id}` מחזיר `{ "status": "..." }`.
- שולחים את `messageId` כ-`reference`, כדי שאם הספק תומך בכך ניתן יהיה לזהות כפילויות.
- **אם השרת של הספק לא זמין בזמן הפיתוח:** הבדיקות משתמשות ב-fake של `ISmsProviderClient`, ואין צורך לבנות שרת מדומה משלנו.

---

## 12. ממשק משתמש (Angular)

עמוד אחד (`/`) עם שני חלקים:

1. **טופס שליחה** (Reactive Forms):
   - שדה טלפון ו-textarea לתוכן, עם מונה תווים ומקטעים בזמן אמת (אותם כללים כמו בשרת, לצורכי תצוגה בלבד).
   - ולידציה בצד הלקוח, שהיא נוחות למשתמש בלבד. מקור האמת הוא השרת, ושגיאות `400` מוצגות ליד השדות.
   - כפתור "שלח" ננעל בזמן שליחה. בכל פתיחה של הטופס נוצר `Idempotency-Key` חדש (`crypto.randomUUID()`), כך שלחיצה כפולה לא יוצרת שתי הודעות.
2. **טבלת הודעות:** זמן יצירה, נמען (מספר ושמות העובדים אם יש), תוכן מקוצר, סטטוס (תג צבעוני), ניסיונות ושגיאה אחרונה.
   - Dropdown לסינון לפי סטטוס.
   - רענון אוטומטי כל 3 שניות כל עוד יש הודעות במצב `Pending` או `Sending`.

טכני: standalone components, signals ו-`HttpClient`. בלי ספריית UI כבדה, CSS פשוט עם `dir="rtl"`. ה-URL של ה-API מוגש דרך nginx כ-proxy (`/api` → `api:8080`), כך שאין צורך ב-CORS.

---

## 13. בדיקות אוטומטיות

פרויקט `tests/SmsService.Tests` (xUnit). בודקים את **הלוגיקה הקריטית**, לא כל שורה.

### `PhoneNumberNormalizerTests`
- `050-1234567`, `0501234567`, `+972501234567` ו-`972 50 123 4567` מנורמלים כולם ל-`+972501234567`.
- מספר נייח (`02-6543210`) נדחה.
- ריק, אותיות ומספר קצר מדי נדחים.
- מספר זר (`+14155550100`) נדחה.

### `MessageBodyRulesTests`
- גוף ריק או גוף שכולו רווחים נדחה.
- 670 תווים עובר, 671 נדחה.
- 160 תווי GSM הם מקטע אחד ו-161 הם שני מקטעים.
- 70 תווים בעברית הם מקטע אחד ו-71 הם שני מקטעים.

### `MessageStatusTests` (מעברי סטטוס)
- `Pending → Sending → Sent` תקין.
- שגיאה זמנית בניסיון הראשון מחזירה ל-`Pending` עם `NextAttemptAt` עתידי ו-`Attempts == 1`.
- שגיאה זמנית בניסיון השלישי מעבירה ל-`Failed`.
- שגיאה קבועה מעבירה ל-`Failed` מיד.
- `MarkSent` על הודעה שאינה במצב `Sending` זורק חריגה.

### `SendWorkerTests` (עם fake provider ו-SQL Server אמיתי דרך `Testcontainers.MsSql`, כי `rowversion` ו-filtered index לא נתמכים ב-SQLite או ב-InMemory)
- הודעה `Pending` נשלחת ועוברת ל-`Sent` עם `ProviderMessageId`.
- ספק שזורק timeout גורם ל-retry ובסוף ל-`Failed` אחרי 3 ניסיונות.
- הודעה `Sending` תקועה חוזרת ל-`Pending` בעלייה.
- כישלון של הודעה אחת לא מונע שליחה של ההודעה הבאה.

### `EmployeeSeederTests`
- שלושה מופעים של אותו עובד עם שני טלפונים שונים (אחד מהם כפול) נטענים כעובד אחד עם 2 טלפונים.
- אותו טלפון אצל שני עובדים שונים נשמר אצל שניהם.

### בדיקת API אחת (WebApplicationFactory)
- `POST` עם אותו `Idempotency-Key` פעמיים מחזיר את אותו `id`, ובטבלה נשמרת הודעה אחת בלבד.

הרצה: `dotnet test`.

---

## 14. הרצה מקומית בפקודה אחת

```bash
docker compose up --build
```

`docker-compose.yml` כולל:

| שירות | Image / Build | פורט |
|---|---|---|
| `db` | `mcr.microsoft.com/mssql/server:2025-latest`, עם `ACCEPT_EULA=Y` וסיסמה מ-`.env`, ו-healthcheck באמצעות `sqlcmd` | 1433 |
| `api` | `src/SmsService.Api/Dockerfile` (multi-stage, `mcr.microsoft.com/dotnet/sdk:10.0` ו-`aspnet:10.0`). מחכה ל-`db` healthy | 8080 |
| `web` | `src/sms-web/Dockerfile` (build ב-`node:24-alpine` והגשה ב-`nginx:alpine`) | 4200 |

- ה-API מריץ `Database.Migrate()` ואת ה-seed בעלייה, ולכן אין צעדים ידניים.
- כתובת הספק המדומה נקבעת דרך `SMS_PROVIDER_URL` בקובץ `.env`. ברירת המחדל היא `http://host.docker.internal:<port>`, כדי לפנות לשרת הספק שרץ על המחשב המקומי.
- מצורף `.env.example`. **אין לשמור סיסמאות אמיתיות ב-repo.**
- כתובות: ממשק ב-`http://localhost:4200` ו-Swagger ב-`http://localhost:8080/swagger`.

הרצה בלי Docker (לפיתוח): `dotnet run --project src/SmsService.Api` ו-`npm start` בתיקיית `src/sms-web`. במצב הזה צריך SQL Server זמין לפי connection string ב-`appsettings.Development.json`.

---

## 15. לוגים ושגיאות

- לוגים מובנים מ-`ILogger`, מוצגים ב-console של הקונטיינר.
- Middleware גלובלי לשגיאות שמחזיר `ProblemDetails` עם `traceId`, בלי stack trace ללקוח.
- לא רושמים בלוג תוכן הודעות ומספרי טלפון מלאים.

---

## 16. סדר עבודה מומלץ (וזמן משוער)

| # | שלב | זמן |
|---|---|---|
| 1 | שלד: solution, `global.json`, פרויקט API, פרויקט בדיקות, docker-compose עם DB בלבד | 20 דק' |
| 2 | Domain ו-Validation, כולל הבדיקות שלהם (סעיף 13, שלוש הקבוצות הראשונות) | 45 דק' |
| 3 | DbContext, migrations ו-seeder של עובדים, כולל בדיקה | 30 דק' |
| 4 | Endpoints, כולל Idempotency ו-ProblemDetails | 30 דק' |
| 5 | `SmsProviderClient` ו-`SendWorker`, כולל בדיקות | 45 דק' |
| 6 | Dockerfile ל-API, ובדיקה ש-`docker compose up` עובד מקצה לקצה | 15 דק' |
| 7 | Angular (אופציונלי, רק אם נשאר זמן) | 30 דק' |
| 8 | README | 15 דק' |
| 9 | בדיקה סופית והעלאה ל-GitHub (סעיף 19) | 5 דק' |

אם הזמן נגמר, עוצרים ומתעדים ב-README מה לא הושלם. שלב 7 הוא הראשון שמוותרים עליו.

---

## 17. הגדרת "סיום" (Definition of Done)

- [ ] `docker compose up --build` מעלה DB ו-API (ו-Web אם מומש) בלי צעדים ידניים.
- [ ] שלושת ה-endpoints עובדים לפי החוזה שבסעיף 8, כולל קודי שגיאה.
- [ ] קלט לא תקין מחזיר `400` עם הודעות ברורות.
- [ ] הודעות נשמרות ב-SQL Server ושורדות restart.
- [ ] כל הבדיקות שבסעיף 13 עוברות ב-`dotnet test`.
- [ ] הפרויקט הועלה ל-repository ב-GitHub, וכל הקבצים (כולל `README.md` ו-`SPEC.md`) מופיעים בענף `main` (סעיף 19).
- [ ] README מכיל: מה נבנה, הנחות יסוד, החלטות, מה לא נעשה ולמה, מה היה נוסף עם עוד זמן, **הצהרה על שימוש ב-AI** (איפה ולמה) 
ו**זמן שהושקע בפועל**.


---

## 18. מה אסור לעשות

- לא להוסיף אימות, Deploy, CI/CD, Message Broker, Redis, MediatR, AutoMapper או Repository Pattern מעל EF Core.
- לא לפצל לכמה פרויקטים מעבר ל-API, Tests ו-Web.
- לא להמציא שדות או endpoints שלא מופיעים כאן.
- לא לבלוע חריגות בשקט. כל `catch` רושם לוג או מעדכן `LastError`.
- לא לשמור connection string עם סיסמה בקוד.
---

## 19. ניהול גרסאות והעלאה ל-GitHub

### במהלך העבודה
- בתחילת העבודה מאתחלים Git (`git init`) ויוצרים `.gitignore` שמתאים ל-.NET ול-Angular. הוא מחריג את `bin/`, `obj/`, `node_modules/`, `dist/`, `.angular/`, `.vs/`, `*.user` ו-`.env`.
- **commit בסוף כל שלב** מסעיף 16, עם הודעה קצרה וברורה באנגלית שמתארת את השלב (למשל `Add phone and body validation with tests`). כך היסטוריית ה-commits משקפת את סדר העבודה, והבודק יכול לעקוב אחריה.
- לפני כל commit מוודאים ש-`dotnet build` ו-`dotnet test` עוברים.
- אין להכניס ל-Git סיסמאות, connection strings עם סיסמה או קובץ `.env`. נכנס רק `.env.example`.

### בסיום העבודה
1. בודקים שכל הסעיפים ב-Definition of Done (סעיף 17) מתקיימים.
2. בודקים שאין קבצים לא רצויים (`git status`), ושה-repository כולל את `README.md`, `SPEC.md`, `docker-compose.yml`, `.env.example` ואת כל הקוד והבדיקות.
3. מחברים את ה-repository המקומי ל-GitHub ומעלים לענף `main`:
   ```bash
   git remote add origin https://github.com/shulamitlux/sms-service.git
   git push -u origin main
   ```
   כתובת ה-repository נמסרת על ידי המפתח. אם היא לא נמסרה, יש לשאול לפני ההעלאה.
4. אם ההעלאה נכשלת בגלל בעיית רשת, מנסים שוב. אם היא נכשלת בגלל הרשאות או repository שלא קיים, עוצרים ומדווחים למפתח, בלי ליצור repository חדש על דעת עצמך.
5. בסיום מדווחים למפתח את הקישור ל-repository ואת ה-commit האחרון שהועלה.
