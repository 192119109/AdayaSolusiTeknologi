# Order Management API Prototype

Ini prototype REST API buat Order Management pake ASP.NET Core & MS SQL Server. Fokus utama di sini adalah nanganin masalah concurrency, idempotency, dan race conditions.

*(Note: database bakal ke-buat dan ke-seed otomatis pas pertama kali run, jadi tanpa perlu setup manual)*

---

## 1. Idempotency (POST /api/orders)

Di sini saya menggunakan strategi header `Idempotency-Key` di request HTTP POST untuk nyegah double order (kalo user nge-klik tombol submit berkali-kali).

Mekanisme :
- Key bakal disimpan di tabel `IdempotentRequests` (Primary Key = `IdempotencyKey`).
- Status request dicatat dulu sebagai `Processing`. Kalo udah beres, status berubah jadi `Completed` dan simpan copy response HTTP-nya (status code & body).
- Kalo ada request masuk dengan key yang sama tapi statusnya masih `Processing`, API langsung balikin `409 Conflict`.
- Tapi kalau statusnya udah `Completed`, API bakal ngasih response hasil cache tadi secara instan.
*Note: Kalau prosesnya error di tengah jalan, key bakal diapus biar client bisa retry lagi.*

---

## 2. Concurrency Handling

Buat nanganin transaksi barengan, saya menggunakan kombinasi antara **Optimistic Concurrency Control (OCC)** bawaan EF Core + kolom `RowVersion` tipe `timestamp` di SQL Server (Database Constraint).
Alasannya melakukan kombinasi agar validasi dilakukan di 2 sisi yaitu dataabase dan API.Selain itu, kombinasi ini saling terkait antara cara kerja database sql server dan EF Core yang dimana otomatis melakukan pengecekan version data pada kolom RowVersion.
 Alasan memilih OCC adalah agar API tidak full load ketika banyak request yang menyebabkan locking (apabila memilih Pessimistic Concurrency Control). Sedangkan alasan memilih Database Constraint menlakukan penjagaan terakhir pada idempotency-key yang dimana dibuat menjadi primary key yang menyebabkan apabila ada insert yang sama maka terjadi error duplikat.

### Skenario A: Concurrent Stock Deduction (Potong Stok)
Pas ada 2 order masuk barengan buat barang yang sama:
* Transaksi ke-1 berhasil update dan bikin `RowVersion` produk berubah di DB.
* Transaksi ke-2 akan dapet error `DbUpdateConcurrencyException` karena `RowVersion` produk udah berbeda dengan pas awal dibaca.
* **Pencegahan**: Sistem akan nge-rollback transaksi ke-2, lalu melakukan **retry otomatis** (maksimal 3 kali). setiap retry, sistem baca ulang stok terbaru. Kalo emang stok ga cukup pas di-reload, langsung keluar error `422 Unprocessable Entity`. Sehingga Stok tidak akan minus.

### Skenario B: Concurrent Status Update (Ganti Status)
Dua admin ganti status order yang sama di detik yang sama (misal satu jadi Shipped, satunya Cancelled).
* Pengecekan `RowVersion` di tabel `Orders` akan nge-block salah satu transaksi.
* Yang kalah akan langsung dapet error `409 Conflict` (atau `400 Bad Request` kalo statusnya emang udah berubah duluan), biar status order ga jadi acak-acakan.

### Skenario C: Idempotent Create Under Race (POST Barengan)
Kalo ada 2 request create order pake `Idempotency-Key` yang sama persis masuk di milidetik yang sama.
* Database constraint (Primary Key unik) akan nge-lock salah satu.
* Request yang kalah insert akan ke-rollback dan dapet error `409 Conflict` karena ngelanggar kunci unik.

---

## 3. Pencegahan Race Condition Lainnya

Selain 3 skenario wajib di atas, kita juga nanganin 2 skenario ini:

* **Double Stock Refund pas Cancel barengan**
  Kalo ada 2 admin coba nge-cancel order yang sama barengan. Kita cek transisi status order (`Pending`/`Confirmed` -> `Cancelled`) dalam transaksi DB yang dilindungi `RowVersion` pada entitas `Order`. Transaksi kedua akan gagal ganti status karena statusnya udah berubah jadi `Cancelled` dan `RowVersion`-nya beda, jadi stok barang ga akan dikembaliin double.
* **Price Race (Harga Berubah pas Checkout)**
  Pas admin ganti harga produk di tabel `Products` pas ada user lagi checkout. Kita nyegah ini dengan cara nge-copy (snapshot) harga produk yang aktif saat itu langsung ke kolom `OrderItem.Price`. Jadi harga orderan sifatnya historis dan ga akan berubah meskipun harga produk diubah-ubah nanti.

---

## 4. Persistensi Data

Database pake **Microsoft SQL Server** (running di `localhost` pake Integrated Security tanpa password).

Kenapa pake SQL Server? Karena fiturnya sangat native unntuk menangani `rowversion` (optimistic locking otomatis) dan sama-sama dikembangkan oleh Microsoft sehingga plugin nya lebih berkesinambungan.

* **Conn String**: `Server=localhost;Database=OrderManagementDB;Trusted_Connection=True;TrustServerCertificate=True;`

---

## 5. Cara Menjalankan & Test


### Jalankan API:
```bash
dotnet run --project OrderManagementAPI
```

### Jalankan Unit & Integration Test:
```bash
dotnet test
```

