# Banner API Testing Guide - Postman

## Base URL
```
http://192.168.1.9:5020/api/banners
```

## Headers for all requests
```
Content-Type: application/json
```

---

## 1. Create Banners (POST)

### Example 1: Welcome Banner - English
```http
POST http://192.168.1.9:5020/api/banners
Content-Type: application/json

{
  "title": "Welcome to Rolling!",
  "subtitle": "Get Started",
  "description": "<p>Enjoy <strong>10% off</strong> on your first order!</p><p>Use code: <em>WELCOME10</em></p>",
  "lang": "en",
  "path": "/menu"
}
```

### Example 2: Summer Sale - Russian
```http
POST http://192.168.1.9:5020/api/banners
Content-Type: application/json

{
  "title": "Летняя распродажа",
  "subtitle": "Скидки до 30%",
  "description": "<p>🌞 Лето в разгаре!</p><p>Получите скидку до <strong>30%</strong> на все роллы и суши сеты.</p><ul><li>Бесплатная доставка при заказе от 100 000 сум</li><li>Подарок к каждому заказу</li></ul>",
  "lang": "ru",
  "path": "/category/sets"
}
```

### Example 3: New Product - Uzbek with URL
```http
POST http://192.168.1.9:5020/api/banners
Content-Type: application/json

{
  "title": "Yangi mahsulot!",
  "subtitle": "Dragon Roll to'plami",
  "description": "<p>🐉 Yangi <strong>Dragon Roll</strong> to'plamini sinab ko'ring!</p><p>Maxsus taom va ajoyib ta'm.</p>",
  "lang": "uz",
  "path": "/product/dragon-roll",
  "url": "https://example.com/special-offer"
}
```

### Example 4: Free Delivery Promo
```http
POST http://192.168.1.9:5020/api/banners
Content-Type: application/json

{
  "title": "Free Delivery",
  "subtitle": "Limited Time Offer",
  "description": "<p>🚚 Free delivery on all orders!</p><p>Valid until the end of the month.</p><br/><p><strong>Order now and save!</strong></p>",
  "lang": "en",
  "path": "/menu"
}
```

### Example 5: Weekend Special
```http
POST http://192.168.1.9:5020/api/banners
Content-Type: application/json

{
  "title": "Выходные со вкусом",
  "subtitle": "Специальное предложение",
  "description": "<h3>Выходные специально для вас!</h3><p>Закажите 2 сета и получите 3-й <strong>в подарок</strong>!</p><p>&nbsp;</p><p>Акция действует только в субботу и воскресенье.</p>",
  "lang": "ru",
  "path": "/category/sets",
  "url": "https://rollingadmin.uz/weekend-promo"
}
```

### Example 6: Loyalty Program
```http
POST http://192.168.1.9:5020/api/banners
Content-Type: application/json

{
  "title": "Join Our Loyalty Program",
  "subtitle": "Earn Rewards",
  "description": "<p>🎁 Earn points with every order!</p><br/><ul><li>1 point = 1000 sum spent</li><li>100 points = Free delivery</li><li>500 points = Free roll</li></ul><p>&mdash; Start earning today!</p>",
  "lang": "en",
  "path": "/profile/loyalty"
}
```

### Example 7: Happy Hour
```http
POST http://192.168.1.9:5020/api/banners
Content-Type: application/json

{
  "title": "Счастливые часы",
  "subtitle": "14:00 - 17:00",
  "description": "<p>⏰ С 14:00 до 17:00 &ndash; скидка 20%!</p><p>На все роллы и горячие блюда.</p><p>&bull; Только для заказов через приложение<br/>&bull; Не суммируется с другими акциями</p>",
  "lang": "ru",
  "path": "/menu"
}
```

### Example 8: Student Discount
```http
POST http://192.168.1.9:5020/api/banners
Content-Type: application/json

{
  "title": "Talabalar uchun chegirma",
  "subtitle": "15% tejang",
  "description": "<p>📚 Talaba kartangizni ko'rsating va <strong>15% chegirma</strong> oling!</p><p>Har kuni, barcha buyurtmalarda.</p>",
  "lang": "uz",
  "path": "/menu"
}
```

### Example 9: Birthday Special
```http
POST http://192.168.1.9:5020/api/banners
Content-Type: application/json

{
  "title": "Birthday Celebration",
  "subtitle": "Special Gift for You",
  "description": "<p>🎂 It's your birthday month?</p><p>Get a <strong>free dessert</strong> with any order over 50,000 sum!</p><p>&laquo;Because birthdays should be special&raquo;</p>",
  "lang": "en",
  "path": "/menu",
  "url": "https://example.com/birthday"
}
```

### Example 10: New Menu Items
```http
POST http://192.168.1.9:5020/api/banners
Content-Type: application/json

{
  "title": "Новинки в меню",
  "subtitle": "Попробуйте что-то новое",
  "description": "<p>✨ Откройте для себя наши новые блюда!</p><p><strong>Новые вкусы:</strong></p><ol><li>Спайси тунец ролл</li><li>Лосось терияки сет</li><li>Креветка темпура</li></ol><p>Попробуйте все три со скидкой 25%!</p>",
  "lang": "ru",
  "path": "/category/new-items"
}
```

### Example 11: Family Combo
```http
POST http://192.168.1.9:5020/api/banners
Content-Type: application/json

{
  "title": "Oilaviy to'plam",
  "subtitle": "4 kishilik",
  "description": "<p>👨‍👩‍👧‍👦 Butun oila uchun maxsus to'plam!</p><p>60 dona roll, 4 ta ichimlik va 2 ta desert - atiga <strong>250,000 so'm</strong></p><p>Odatdagidan 40% arzonroq!</p>",
  "lang": "uz",
  "path": "/category/family-sets"
}
```

### Example 12: Corporate Catering
```http
POST http://192.168.1.9:5020/api/banners
Content-Type: application/json

{
  "title": "Corporate Catering",
  "subtitle": "For Business Events",
  "description": "<p>💼 Planning a corporate event?</p><p>We offer special packages for:</p><ul><li>Office parties</li><li>Business meetings</li><li>Team building events</li></ul><p>Contact us for custom quotes!</p>",
  "lang": "en",
  "path": "/corporate",
  "url": "https://example.com/corporate-catering"
}
```

---

## 2. Get All Banners (GET)

### Get all banners
```http
GET http://192.168.1.9:5020/api/banners
```

### Get banners by language (Russian)
```http
GET http://192.168.1.9:5020/api/banners?lang=ru
```

### Get banners by language (English)
```http
GET http://192.168.1.9:5020/api/banners?lang=en
```

### Get banners by language (Uzbek)
```http
GET http://192.168.1.9:5020/api/banners?lang=uz
```

---

## 3. Get Single Banner (GET)

```http
GET http://192.168.1.9:5020/api/banners/1
```

---

## 4. Update Banner (PUT)

### Update title and subtitle
```http
PUT http://192.168.1.9:5020/api/banners/1
Content-Type: application/json

{
  "title": "Updated Welcome Banner",
  "subtitle": "New Subtitle"
}
```

### Update description only
```http
PUT http://192.168.1.9:5020/api/banners/1
Content-Type: application/json

{
  "description": "<p>New <strong>updated</strong> description with HTML!</p>"
}
```

### Deactivate banner
```http
PUT http://192.168.1.9:5020/api/banners/1
Content-Type: application/json

{
  "isActive": false
}
```

### Reactivate banner
```http
PUT http://192.168.1.9:5020/api/banners/1
Content-Type: application/json

{
  "isActive": true
}
```

---

## 5. Delete Banner (DELETE)

### Soft delete (deactivate)
```http
DELETE http://192.168.1.9:5020/api/banners/1
```

### Permanent delete (remove from database)
```http
DELETE http://192.168.1.9:5020/api/banners/1/permanent
```

---

## Response Examples

### Success Response (Create/Get Single)
```json
{
  "id": 1,
  "title": "Welcome to Rolling!",
  "subtitle": "Get Started",
  "description": "<p>Enjoy <strong>10% off</strong> on your first order!</p><p>Use code: <em>WELCOME10</em></p>",
  "url": null,
  "lang": "en",
  "path": "/menu",
  "createdAt": "2024-12-06 12:00:00"
}
```

### Success Response (Get All)
```json
{
  "banners": [
    {
      "id": 1,
      "title": "Welcome to Rolling!",
      "subtitle": "Get Started",
      "description": "<p>Enjoy <strong>10% off</strong> on your first order!</p>",
      "url": null,
      "lang": "en",
      "path": "/menu",
      "createdAt": "2024-12-06 12:00:00"
    },
    {
      "id": 2,
      "title": "Летняя распродажа",
      "subtitle": "Скидки до 30%",
      "description": "<p>🌞 Лето в разгаре!</p>",
      "url": null,
      "lang": "ru",
      "path": "/category/sets",
      "createdAt": "2024-12-06 11:30:00"
    }
  ]
}
```

### Error Response
```json
{
  "error": "Banner not found"
}
```

---

## Quick Test Workflow

1. **Create a few banners** using Examples 1-3
2. **Get all banners** to see the list
3. **Get banners by language** (e.g., `?lang=ru`)
4. **Get a specific banner** by ID
5. **Update a banner** (change title or deactivate)
6. **Delete a banner** (soft delete)
7. **Verify** by getting all banners again

---

## Notes

- All banners are sorted by `createdAt` DESC (newest first)
- `isActive` defaults to `true` when creating
- Soft delete sets `isActive` to `false`
- HTML entities are supported: `&nbsp;`, `&mdash;`, `&bull;`, etc.
- Emojis are supported in all text fields
