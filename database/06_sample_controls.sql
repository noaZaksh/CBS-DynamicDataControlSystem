/* =====================================================================
   06 - בקרות לדוגמה
   כל בקרה נשמרת כ-JSON מובנה (עץ תנאים) המתאר את "תנאי החריגה":
   רשומה שמקיימת את התנאי = רשומה חריגה.
   ראו docs/CONTROL_DEFINITION.md לתיאור הפורמט המלא.
   ===================================================================== */
USE DataControlsDb;
GO
SET NOCOUNT ON;
DELETE FROM ctl.ControlRuns;
DELETE FROM ctl.Controls;
GO

INSERT INTO ctl.Controls (Name, Description, Category, Severity, RootEntityKey, DefinitionJson) VALUES
(N'קטינים עם שכר גבוה',
 N'עובדים שגילם נמוך מ-18 ושכרם החודשי גבוה מ-15,000 ש"ח',
 N'שכר', 'High', 'employee',
 N'{"rootEntity":"employee","filter":{"type":"group","id":"g1","logic":"and","children":[
     {"type":"condition","id":"c1","field":"age","operator":"lt","value":{"kind":"constant","value":18}},
     {"type":"condition","id":"c2","field":"monthlySalary","operator":"gt","value":{"kind":"constant","value":15000}}]}}'),

(N'תאריך סיום העסקה לפני תאריך תחילה',
 N'עובדים שתאריך סיום העסקתם מוקדם מתאריך תחילת העסקתם',
 N'תאריכים', 'High', 'employee',
 N'{"rootEntity":"employee","filter":{"type":"group","id":"g1","logic":"and","children":[
     {"type":"condition","id":"c1","field":"endDate","operator":"lt","value":{"kind":"field","field":"startDate"}}]}}'),

(N'עובד פעיל במחלקה לא פעילה',
 N'עובדים פעילים המשויכים למחלקה שאינה פעילה',
 N'שיוך ארגוני', 'Medium', 'employee',
 N'{"rootEntity":"employee","filter":{"type":"group","id":"g1","logic":"and","children":[
     {"type":"condition","id":"c1","field":"isActive","operator":"eq","value":{"kind":"constant","value":true}},
     {"type":"condition","id":"c2","field":"department.isActive","operator":"eq","value":{"kind":"constant","value":false}}]}}'),

(N'חסרים נתוני חובה לעובד פעיל',
 N'עובד פעיל שחסרים לו ת"ז, דוא"ל, שכר או שיוך למחלקה',
 N'שלמות נתונים', 'Medium', 'employee',
 N'{"rootEntity":"employee","filter":{"type":"group","id":"g1","logic":"and","children":[
     {"type":"condition","id":"c1","field":"isActive","operator":"eq","value":{"kind":"constant","value":true}},
     {"type":"group","id":"g2","logic":"or","children":[
        {"type":"condition","id":"c2","field":"idNumber","operator":"isEmpty"},
        {"type":"condition","id":"c3","field":"email","operator":"isEmpty"},
        {"type":"condition","id":"c4","field":"monthlySalary","operator":"isEmpty"},
        {"type":"condition","id":"c5","field":"department.departmentName","operator":"isEmpty"}]}]}}'),

(N'עובד לא פעיל ללא תאריך סיום',
 N'עובד שסומן כלא פעיל אך לא הוזן לו תאריך סיום העסקה',
 N'תאריכים', 'Low', 'employee',
 N'{"rootEntity":"employee","filter":{"type":"group","id":"g1","logic":"and","children":[
     {"type":"condition","id":"c1","field":"isActive","operator":"eq","value":{"kind":"constant","value":false}},
     {"type":"condition","id":"c2","field":"endDate","operator":"isEmpty"}]}}'),

(N'עובד פעיל עם תאריך סיום שעבר או תאריך תחילה עתידי',
 N'סתירה בין סטטוס "פעיל" לבין תאריכי ההעסקה',
 N'תאריכים', 'Medium', 'employee',
 N'{"rootEntity":"employee","filter":{"type":"group","id":"g1","logic":"and","children":[
     {"type":"condition","id":"c1","field":"isActive","operator":"eq","value":{"kind":"constant","value":true}},
     {"type":"group","id":"g2","logic":"or","children":[
        {"type":"condition","id":"c2","field":"endDate","operator":"lt","value":{"kind":"today","offsetDays":0}},
        {"type":"condition","id":"c3","field":"startDate","operator":"gt","value":{"kind":"today","offsetDays":0}}]}]}}'),

(N'שכר לא תקין',
 N'עובד פעיל ששכרו החודשי אפס או שלילי',
 N'שכר', 'High', 'employee',
 N'{"rootEntity":"employee","filter":{"type":"group","id":"g1","logic":"and","children":[
     {"type":"condition","id":"c1","field":"isActive","operator":"eq","value":{"kind":"constant","value":true}},
     {"type":"condition","id":"c2","field":"monthlySalary","operator":"lte","value":{"kind":"constant","value":0}}]}}'),

(N'כתובת דוא"ל לא תקינה',
 N'דוא"ל שהוזן אך אינו מכיל את התו @',
 N'שלמות נתונים', 'Low', 'employee',
 N'{"rootEntity":"employee","filter":{"type":"group","id":"g1","logic":"and","children":[
     {"type":"condition","id":"c1","field":"email","operator":"isNotEmpty"},
     {"type":"condition","id":"c2","field":"email","operator":"notContains","value":{"kind":"constant","value":"@"}}]}}'),

(N'מחלקה פעילה ללא עובדים פעילים',
 N'מחלקה פעילה שאין בה אף עובד פעיל (בדיקה על קשר "אחד לרבים")',
 N'שיוך ארגוני', 'Medium', 'department',
 N'{"rootEntity":"department","filter":{"type":"group","id":"g1","logic":"and","children":[
     {"type":"condition","id":"c1","field":"isActive","operator":"eq","value":{"kind":"constant","value":true}},
     {"type":"related","id":"r1","relation":"employees","quantifier":"none","filter":{"type":"group","id":"g2","logic":"and","children":[
        {"type":"condition","id":"c2","field":"isActive","operator":"eq","value":{"kind":"constant","value":true}}]}}]}}'),

(N'מחלקה פעילה ללא מנהל פעיל',
 N'מחלקה פעילה שלא הוגדר לה מנהל, או שהמנהל אינו עובד פעיל',
 N'שיוך ארגוני', 'Medium', 'department',
 N'{"rootEntity":"department","filter":{"type":"group","id":"g1","logic":"and","children":[
     {"type":"condition","id":"c1","field":"isActive","operator":"eq","value":{"kind":"constant","value":true}},
     {"type":"group","id":"g2","logic":"or","children":[
        {"type":"condition","id":"c2","field":"manager.fullName","operator":"isEmpty"},
        {"type":"condition","id":"c3","field":"manager.isActive","operator":"eq","value":{"kind":"constant","value":false}}]}]}}'),

(N'עובד פעיל משובץ לפרויקט שהסתיים',
 N'עובד פעיל שיש לו שיבוץ לפרויקט שהסטטוס שלו "הסתיים"',
 N'פרויקטים', 'Low', 'employee',
 N'{"rootEntity":"employee","filter":{"type":"group","id":"g1","logic":"and","children":[
     {"type":"condition","id":"c1","field":"isActive","operator":"eq","value":{"kind":"constant","value":true}},
     {"type":"related","id":"r1","relation":"assignments","quantifier":"any","filter":{"type":"group","id":"g2","logic":"and","children":[
        {"type":"condition","id":"c2","field":"project.status","operator":"eq","value":{"kind":"constant","value":"הסתיים"}}]}}]}}'),

(N'שיבוץ לא תקין לפרויקט',
 N'אחוז שיבוץ חסר או מעל 100%',
 N'פרויקטים', 'Medium', 'assignment',
 N'{"rootEntity":"assignment","filter":{"type":"group","id":"g1","logic":"or","children":[
     {"type":"condition","id":"c1","field":"allocationPercent","operator":"gt","value":{"kind":"constant","value":100}},
     {"type":"condition","id":"c2","field":"allocationPercent","operator":"isEmpty"}]}}'),

(N'פרויקט עם תאריך סיום לפני התחלה',
 N'פרויקטים שתאריך הסיום שלהם מוקדם מתאריך ההתחלה',
 N'תאריכים', 'Medium', 'project',
 N'{"rootEntity":"project","filter":{"type":"group","id":"g1","logic":"and","children":[
     {"type":"condition","id":"c1","field":"endDate","operator":"lt","value":{"kind":"field","field":"startDate"}}]}}');
GO
