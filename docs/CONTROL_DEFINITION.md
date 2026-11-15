# פורמט הגדרת בקרה

הגדרת בקרה היא אובייקט JSON שמתאר את **תנאי החריגה**: כל רשומה של `rootEntity` שמקיימת את `filter` מוצגת כחריגה.
זה הפורמט היחיד במערכת. הוא נשמר ב-`ctl.Controls.DefinitionJson`, נשלח מהעורך, מוחזר מה-AI ומתורגם ל-SQL.

```json
{
  "schemaVersion": 1,
  "rootEntity": "employee",
  "filter": { "type": "group", "logic": "and", "children": [ ... ] }
}
```

## צמתים

### קבוצה (`group`)
| שדה | ערכים |
|---|---|
| `logic` | `and` (כל התנאים) / `or` (לפחות אחד) |
| `negate` | `true` = "לא מתקיים ש..." |
| `children` | צמתים (קבוצות, תנאים, תנאי קשר) |

### תנאי (`condition`)
| שדה | תיאור |
|---|---|
| `field` | נתיב עסקי: `age`, או דרך קשרי ManyToOne: `department.isActive`, `department.manager.isActive` |
| `operator` | ראו טבלה |
| `value` / `valueTo` | `{ "kind": "constant", "value": 18 }` · `{ "kind": "field", "field": "startDate" }` · `{ "kind": "today", "offsetDays": -30 }` · `{ "kind": "list", "values": ["a","b"] }` |

### תנאי על רשומות קשורות (`related`)
עבור קשרי OneToMany ("מחלקה ← עובדים"):
```json
{ "type": "related", "relation": "employees", "quantifier": "none",
  "filter": { "type": "group", "children": [ { "type": "condition", "field": "isActive", "operator": "eq", "value": { "kind": "constant", "value": true } } ] } }
```
`quantifier`: `any` = קיימת לפחות רשומה אחת שעומדת בתנאים, `none` = לא קיימת אף רשומה כזו. התנאים הפנימיים מתייחסים לישות היעד.

## אופרטורים

| מפתח | תווית | סוגים | ערכים | השוואה לשדה |
|---|---|---|---|---|
| `eq` / `neq` | שווה ל / שונה מ | הכל | 1 | ✓ |
| `gt` `gte` `lt` `lte` | גדול / קטן (בתאריכים: מאוחר / מוקדם) | מספר, תאריך, טקסט | 1 | ✓ |
| `between` | בין | מספר, תאריך | 2 | |
| `in` / `notIn` | אחד מ / אינו אחד מ | טקסט, מספר | רשימה | |
| `contains` `notContains` `startsWith` `endsWith` | מכיל / מתחיל ב... | טקסט | 1 | |
| `isEmpty` / `isNotEmpty` | ריק או חסר / מלא | הכל | 0 | |

הקטלוג נחשף ב-`GET /api/metadata/model`, והממשק נבנה ממנו, כך שאין בצד הלקוח רשימת אופרטורים קשיחה.

## דוגמה מלאה ← SQL

"עובד פעיל, וחסרים לו ת"ז או דוא"ל":
```json
{ "rootEntity": "employee", "filter": { "type": "group", "id": "g1", "logic": "and", "children": [
  { "type": "condition", "id": "c1", "field": "isActive", "operator": "eq", "value": { "kind": "constant", "value": true } },
  { "type": "group", "id": "g2", "logic": "or", "children": [
    { "type": "condition", "id": "c2", "field": "idNumber", "operator": "isEmpty" },
    { "type": "condition", "id": "c3", "field": "email", "operator": "isEmpty" } ] } ] } }
```
מתורגם ל- (חלק 3 מתוך 3):
```sql
SELECT TOP (5000)
    t0.[EmployeeId] AS [__key], t0.[FullName] AS [__title], t0.[IsActive] AS [d_isActive],
    t0.[IdNumber] AS [e_0], t0.[Email] AS [e_1],
    CASE WHEN t0.[IsActive] = @p0 THEN 1 ELSE 0 END AS [__f_c1],
    CASE WHEN (t0.[IdNumber] IS NULL OR LTRIM(RTRIM(t0.[IdNumber])) = N'') THEN 1 ELSE 0 END AS [__f_c2],
    ...
FROM [hr].[vw_EmployeesEnriched] AS t0
WHERE (t0.[IsActive] = @p0 AND ((t0.[IdNumber] IS NULL OR ...) OR (t0.[Email] IS NULL OR ...)))
ORDER BY t0.[EmployeeId];
```
העמודות `__f_*` מאפשרות להציג לכל רשומה **איזה** תנאי ב"או" התקיים, למשל "דואר אלקטרוני: חסר".

## ולידציה
`ControlDefinitionValidator` בודק מול מילון הנתונים הנוכחי:
- הישות, השדות והקשרים קיימים, ובנתיב שדה מופיעים רק קשרי ManyToOne.
- האופרטור מתאים לסוג הנתון, יש ערך כשצריך, וניתן להמיר אותו לסוג השדה.
- בהשוואה בין שדות שני השדות מאותו סוג, ו"היום" משמש רק לתאריכים.
- מגבלות מורכבות: 60 צמתים, 6 רמות קינון ו-200 ערכים ברשימה.
- מזהי צמתים ייחודיים (`DefinitionNormalizer` משלים מזהים חסרים).

השגיאות מוחזרות בעברית עם `nodeId`, והעורך מסמן את השורה הרלוונטית.
