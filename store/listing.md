# Microsoft Store submission: Arshid Calendar

Text and answers to paste into Partner Center. The package language is **fa-IR**, so the Persian listing is required. The English listing is optional but helps people searching in English.

Images are in [`images/`](images/) and are made by `dotnet run tools/generate-assets.cs -- .`

| File | Partner Center field |
|---|---|
| `screenshot-1-desktop.png`, `screenshot-2-widgets-board.png` (1920×1080) | Store listing → Screenshots → Desktop |
| `boxart-1080x1080.png` / `boxart-2160x2160.png` | Store listing → Store logos → 1:1 box art |
| `logo-300x300.png` | Store listing → Store logos → 1:1 app tile icon |

---

## Properties

| Field | Value |
|---|---|
| Category | **Productivity** (subcategory: none) |
| Privacy policy URL | `https://github.com/zandiarash/Arshid-Calendar/blob/main/PRIVACY.md` (the repository must be public) |
| Website | `https://github.com/zandiarash/Arshid-Calendar` |
| Support contact | an email address or the repository's Issues page |
| Product declarations | Leave all unchecked, except "This app has been tested to meet accessibility guidelines" (only if you've tested it) |
| System requirements | Minimum: Windows 11 version 22H2. Memory 4 GB. Architecture x64, ARM64 |
| Display mode | Windows Desktop |

## Age ratings (IARC questionnaire)

Category: **Utility / productivity**. Answer **No** to every content question: no violence, no user-generated content, no sharing of location or personal data, no purchases, no gambling, and no unrestricted internet access. The expected rating is 3+ / Everyone.

## Submission options → Restricted capabilities

`runFullTrust`, justification:

> Arshid Calendar is a Win32 (WPF) desktop application. The package contains the desktop widget and a Windows 11 Widgets Board provider, which is an out-of-process COM server activated by the Widgets host. Full trust is required for Win32 desktop apps.

## Notes for certification

> The app has two parts. (1) The desktop widget: launch "تقویم آرشید" from Start. It shows a Persian calendar window and a tray icon; left-click the tray icon for the calendar flyout, right-click for the menu. (2) The Widgets Board widget: press Win+W, click "+", and add "تقویم آرشید" (small, medium and large sizes). No account or network access is needed.

---

## Store listing: Persian (fa-IR)

**Product name:** تقویم آرشید

**Short description** (shown at the top of the listing):

> تقویم شمسی زیبا برای ویندوز ۱۱: ویجت دسکتاپ، تقویم کنار ساعت نوار وظیفه و ابزارک صفحهٔ ویجت‌ها، با مناسبت‌ها و تعطیلات رسمی.

**Description:**

> تقویم آرشید یک تقویم هجری شمسی برای ویندوز ۱۱ است که با طراحی بومی ویندوز ساخته شده: پس‌زمینهٔ شیشه‌ای، گوشه‌های گرد، حالت روشن و تیره و فونت وزیرمتن.
>
> امروز را در یک نگاه ببینید: روز هفته، تاریخ شمسی، میلادی و قمری، ساعت زنده و پیشرفت سال تا نوروز. جدول ماه از شنبه شروع می‌شود؛ جمعه‌ها و تعطیلات رسمی قرمز هستند و روزهای دارای مناسبت با یک نقطه مشخص شده‌اند.
>
> تمام تعطیلات رسمی ایران (شمسی و قمری) همراه با شب یلدا، چهارشنبه‌سوری، مهرگان، تیرگان، سده و روزهای بزرگداشت فردوسی، سعدی، حافظ و دیگر بزرگان در تقویم آمده است و مناسبت بعدی همیشه با شمارش معکوس نمایش داده می‌شود.
>
> سه شکل استفاده:
> • ویجت دسکتاپ: روی دسکتاپ، همیشه رو یا یک پنجرهٔ معمولی.
> • تقویم نوار وظیفه: با کلیک روی آیکون کنار ساعت، تقویم فارسی به سبک تقویم خود ویندوز باز می‌شود.
> • ابزارک صفحهٔ ویجت‌ها (Win + W) در سه اندازهٔ کوچک، متوسط و بزرگ.
>
> رنگ‌ها با فصل تغییر می‌کنند: سبز بهار، آفتاب تابستان، نارنجی پاییز و آبی زمستان.
>
> بدون تبلیغات، بدون نیاز به اینترنت و بدون جمع‌آوری هیچ داده‌ای.

**Product features** (one per line):

- تاریخ شمسی، میلادی و قمری در یک نگاه
- ساعت زنده و پیام خوشامد بر اساس زمان روز
- نوار پیشرفت سال و شمارش روزها تا نوروز
- همهٔ تعطیلات رسمی و مناسبت‌های ملی و مذهبی ایران
- نمایش مناسبت بعدی با شمارش معکوس
- تقویم فارسی کنار ساعت نوار وظیفه
- ابزارک صفحهٔ ویجت‌های ویندوز ۱۱ در سه اندازه
- حالت روشن و تیره همگام با ویندوز
- رنگ‌های فصلی و پس‌زمینهٔ شیشه‌ای
- اجرا همراه با ویندوز (اختیاری)
- بدون تبلیغات و بدون نیاز به اینترنت

**Keywords** (up to 7): `تقویم` · `تقویم شمسی` · `تقویم فارسی` · `جلالی` · `ویجت` · `Persian calendar` · `Jalali`

**Screenshot captions:**
1. ویجت دسکتاپ در حالت تیره و روشن
2. ابزارک صفحهٔ ویجت‌های ویندوز ۱۱

**What's new in this version** (1.0.0):

> نخستین انتشار تقویم آرشید در فروشگاه مایکروسافت.

**Copyright:** © 2026 Arshid Calendar

---

## Store listing: English (en-US), optional

To add it, go to Store listings → Manage additional languages → English (United States).

**Product name:** Arshid Calendar – Persian Calendar

**Short description:**

> A beautiful Persian (Jalali) calendar for Windows 11: desktop widget, taskbar calendar flyout and Widgets Board widget, with Iranian holidays and occasions.

**Description:**

> Arshid Calendar is a Persian (Solar Hijri / Jalali) calendar built to feel native on Windows 11: acrylic glass, rounded corners, light and dark themes, and the Vazirmatn font.
>
> See today at a glance: weekday, Persian, Gregorian and lunar Hijri dates, a live clock and the year's progress toward Nowruz. The Saturday-first month grid marks Fridays and official holidays in red and puts a dot on days with occasions.
>
> All official Iranian holidays (solar and lunar) are included, plus Yalda, Chaharshanbe Suri, Mehregan, Tirgan, Sadeh and the days honouring Ferdowsi, Saadi, Hafez and others. The next occasion is always shown with a countdown.
>
> Three ways to use it:
> • Desktop widget: pinned to the desktop, always on top, or a normal window.
> • Taskbar flyout: click the tray icon for a Persian calendar in the style of the Windows clock calendar.
> • Windows 11 Widgets Board (Win + W) in small, medium and large sizes.
>
> Colours follow the seasons: spring green, summer sun, autumn ember and winter ice.
>
> No ads, no internet connection required, and no data collected.

**Product features:**

- Persian, Gregorian and lunar Hijri dates at a glance
- Live clock with a time-of-day greeting
- Year progress bar and days until Nowruz
- All official Iranian holidays and national occasions
- Next occasion with a countdown
- Persian calendar flyout from the taskbar
- Windows 11 Widgets Board widget in three sizes
- Light and dark themes that follow Windows
- Seasonal colours and a glass background
- Optional start with Windows
- No ads, works offline

**Keywords:** `Persian calendar` · `Jalali` · `Shamsi` · `Solar Hijri` · `Iranian calendar` · `calendar widget` · `Farsi`

**What's new:** First Microsoft Store release.
