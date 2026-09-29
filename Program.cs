using System;
using System.IO;
using System.Text;
using System.Data.SQLite;

namespace SQLiteLabApp
{
    internal class Program
    {
        private static readonly string databaseName = "StudentsDB.sqlite";
        private static readonly string connString = $"Data Source={databaseName};Version=3;";

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            SetupDatabase();

            while (true)
            {
                Console.WriteLine("\n");
                Console.WriteLine("               ГОЛОВНЕ МЕНЮ               ");
                Console.WriteLine("");
                Console.WriteLine("1. Заповнити БД тестовими записами (INSERT)");
                Console.WriteLine("2. Перевірка обмеження foreign key (FK Error)");
                Console.WriteLine("3. Зміна квитка та віку за роком (CASCADE UPDATE)");
                Console.WriteLine("4. Видалення студентів без плану (RESTRICT DELETE)");
                Console.WriteLine("5. Вивести вміст обох таблиць (SELECT)");
                Console.WriteLine("0. Завершити роботу");
                Console.Write("\nОберіть пункт: ");

                string userAction = Console.ReadLine();
                switch (userAction)
                {
                    case "1":
                        SeedInitialData();
                        break;
                    case "2":
                        TestForeignKeyConstraint();
                        break;
                    case "3":
                        UpdateStudentInfo();
                        break;
                    case "4":
                        DeleteStudentsByCity();
                        break;
                    case "5":
                        DisplayAllRecords();
                        break;
                    case "0":
                        return;
                    default:
                        Console.WriteLine("Помилка");
                        break;
                }
            }
        }

        private static void RunNonQuery(string sql, SQLiteConnection connection)
        {
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.ExecuteNonQuery();
            }
        }

        private static void SetupDatabase()
        {
            if (!File.Exists(databaseName))
            {
                SQLiteConnection.CreateFile(databaseName);
            }

            using (var connection = new SQLiteConnection(connString))
            {
                connection.Open();
                RunNonQuery("PRAGMA case_sensitive_like = ON;", connection);
                RunNonQuery("PRAGMA foreign_keys = ON;", connection);

                string tablesAndTriggers = @"
                    CREATE TABLE IF NOT EXISTS Student (
                        StudentCard TEXT PRIMARY KEY NOT NULL,
                        FullName TEXT NOT NULL,
                        BirthPlace TEXT NOT NULL,
                        BirthYear INT NOT NULL,
                        Age INT NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS StudyPlan (
                        SubjectID INTEGER PRIMARY KEY AUTOINCREMENT,
                        SubjectName TEXT NOT NULL,
                        HoursCount INT NOT NULL,
                        Grade INT CHECK (Grade >= 1 AND Grade <= 100),
                        StudentCard TEXT NOT NULL,
                        FOREIGN KEY (StudentCard) REFERENCES Student(StudentCard)
                    );

                    DROP TRIGGER IF EXISTS trigger_Student_Update_Cascade;
                    CREATE TRIGGER trigger_Student_Update_Cascade
                    AFTER UPDATE OF StudentCard ON Student
                    BEGIN
                        UPDATE StudyPlan
                        SET StudentCard = NEW.StudentCard
                        WHERE StudentCard = OLD.StudentCard;
                    END;

                    DROP TRIGGER IF EXISTS trigger_Student_Delete_Restrict;
                    CREATE TRIGGER trigger_Student_Delete_Restrict
                    BEFORE DELETE ON Student
                    BEGIN
                        SELECT CASE
                            WHEN (SELECT COUNT(*) FROM StudyPlan WHERE StudentCard = OLD.StudentCard) > 0
                            THEN RAISE(ABORT, 'Заборонено видаляти: у студента є дисципліни в плані!')
                        END;
                    END;
                ";
                RunNonQuery(tablesAndTriggers, connection);
            }
        }

        private static void SeedInitialData()
        {
            using (var connection = new SQLiteConnection(connString))
            {
                connection.Open();
                RunNonQuery("PRAGMA foreign_keys = ON;", connection);

                string seedSql = @"
                    INSERT OR REPLACE INTO Student VALUES ('SN-201', 'Кличко О. В.', 'Київ', 2006, 18);
                    INSERT OR REPLACE INTO Student VALUES ('SN-202', 'Саша С. С.', 'Одеса', 2006, 18);
                    INSERT OR REPLACE INTO Student VALUES ('SN-203', 'Громляк О. В.', 'Тернопіль', 2005, 19);

                    INSERT INTO StudyPlan (SubjectName, HoursCount, Grade, StudentCard) VALUES 
                        ('Архітектура комп''ютерів', 90, 85, 'SN-201'),
                        ('Дискретна математика', 120, 92, 'SN-201'),
                        ('ООП', 120, 90, 'SN-202'),
                        ('Організація баз даних', 90, 78, 'SN-202');
                ";
                RunNonQuery(seedSql, connection);
                Console.WriteLine(">> Тестові дані успішно записано.");
            }
        }

        private static void TestForeignKeyConstraint()
        {
            using (var connection = new SQLiteConnection(connString))
            {
                connection.Open();
                RunNonQuery("PRAGMA foreign_keys = ON;", connection);

                try
                {
                    string badQuery = "INSERT INTO StudyPlan (SubjectName, HoursCount, Grade, StudentCard) VALUES (@sub, @hrs, @grd, @card);";
                    using (var command = new SQLiteCommand(badQuery, connection))
                    {
                        command.Parameters.AddWithValue("@sub", "Вища математика");
                        command.Parameters.AddWithValue("@hrs", 60);
                        command.Parameters.AddWithValue("@grd", 75);
                        command.Parameters.AddWithValue("@card", "NO_SUCH_STUDENT");
                        command.ExecuteNonQuery();
                    }
                }
                catch (SQLiteException ex)
                {
                    Console.WriteLine($"\n[СПРАЦЮВАВ ЗАХИСТ ЗОВНІШНЬОГО КЛЮЧА]:\n{ex.Message}");
                }
            }
        }

        private static void UpdateStudentInfo()
        {
            Console.Write("Введіть рік народження для вибірки: ");
            if (!int.TryParse(Console.ReadLine(), out int targetYear)) targetYear = 2006;

            using (var connection = new SQLiteConnection(connString))
            {
                connection.Open();
                RunNonQuery("PRAGMA foreign_keys = ON;", connection);

                string updateSql = "UPDATE Student SET StudentCard = StudentCard || '_V2', Age = Age + 1 WHERE BirthYear = @year;";
                using (var command = new SQLiteCommand(updateSql, connection))
                {
                    command.Parameters.AddWithValue("@year", targetYear);
                    int updatedRows = command.ExecuteNonQuery();
                    Console.WriteLine($">> Оновлено записів студентів: {updatedRows}.");
                }
            }
        }

        private static void DeleteStudentsByCity()
        {
            Console.Write("Введіть місто для видалення: ");
            string targetCity = Console.ReadLine();

            using (var connection = new SQLiteConnection(connString))
            {
                connection.Open();
                RunNonQuery("PRAGMA foreign_keys = ON;", connection);

                try
                {
                    string deleteSql = @"
                        DELETE FROM Student 
                        WHERE BirthPlace = @city 
                          AND StudentCard NOT IN (SELECT DISTINCT StudentCard FROM StudyPlan);
                    ";
                    using (var command = new SQLiteCommand(deleteSql, connection))
                    {
                        command.Parameters.AddWithValue("@city", targetCity);
                        int deletedCount = command.ExecuteNonQuery();
                        Console.WriteLine($">> Видалено студентів: {deletedCount}");
                    }
                }
                catch (SQLiteException ex)
                {
                    Console.WriteLine($"[ПОМИЛКА ОБМЕЖЕННЯ RESTRICT]: {ex.Message}");
                }
            }
        }

        private static void DisplayAllRecords()
        {
            using (var connection = new SQLiteConnection(connString))
            {
                connection.Open();

                Console.WriteLine("\n--- ТАБЛИЦЯ: Student (Головна) ---");
                using (var cmd = new SQLiteCommand("SELECT StudentCard, FullName, BirthPlace, BirthYear, Age FROM Student", connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Console.WriteLine($"Квиток: {reader["StudentCard"],-14} | ПІБ: {reader["FullName"],-18} | Місто: {reader["BirthPlace"],-10} | Рік: {reader["BirthYear"]} | Вік: {reader["Age"]}");
                    }
                }

                Console.WriteLine("\n--- ТАБЛИЦЯ: StudyPlan (Підлегла) ---");
                using (var cmd = new SQLiteCommand("SELECT SubjectID, SubjectName, HoursCount, Grade, StudentCard FROM StudyPlan", connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Console.WriteLine($"ID: {reader["SubjectID"],-3} | Предмет: {reader["SubjectName"],-30} | Годин: {reader["HoursCount"],-4} | Бал: {reader["Grade"],-3} | Студент (FK): {reader["StudentCard"]}");
                    }
                }
            }
        }
    }
}