USE LibrarySystemDb;
GO

DECLARE @Salt NVARCHAR(64) = N'LIBRARY_SYSTEM_2026';

INSERT INTO dbo.Books (Isbn, Title, Publisher, Author, TotalCopies, AvailableCopies, IsBorrowable)
VALUES
(N'9787111128069', N'数据库系统概论', N'机械工业出版社', N'王珊 萨师煊', 8, 6, 1),
(N'9787302423287', N'数据库系统基础教程', N'清华大学出版社', N'Jeffrey D. Ullman', 5, 5, 1),
(N'9787115428028', N'深入理解计算机系统', N'人民邮电出版社', N'Randal E. Bryant', 4, 3, 1),
(N'9787115546081', N'算法导论', N'人民邮电出版社', N'Thomas H. Cormen', 6, 6, 1),
(N'9787121317985', N'计算机网络 自顶向下方法', N'电子工业出版社', N'James F. Kurose', 5, 4, 1),
(N'9787111213826', N'软件工程 实践者的研究方法', N'机械工业出版社', N'Roger S. Pressman', 3, 3, 1),
(N'9787302330646', N'操作系统概念', N'高等教育出版社', N'Abraham Silberschatz', 4, 4, 1),
(N'9787115417305', N'代码整洁之道', N'人民邮电出版社', N'Robert C. Martin', 2, 2, 1);

INSERT INTO dbo.Readers (ReaderCardNo, Name, Gender, Title, MaxBorrowCount, BorrowedCount, Department, Phone)
VALUES
(N'SYSU-SZ-2024001', N'陈思远', N'男', N'本科生', 5, 1, N'计算机学院', N'13800010001'),
(N'SYSU-SZ-2024002', N'林若曦', N'女', N'本科生', 5, 1, N'电子与通信工程学院', N'13800010002'),
(N'SYSU-SZ-2024003', N'许知行', N'男', N'研究生', 8, 0, N'人工智能学院', N'13800010003'),
(N'SYSU-SZ-2024004', N'周清禾', N'女', N'教师', 10, 0, N'软件工程学院', N'13800010004'),
(N'SYSU-SZ-2024005', N'何嘉宁', N'其他', N'本科生', 5, 0, N'数据科学学院', N'13800010005');

INSERT INTO dbo.Accounts (Username, PasswordHash, PasswordSalt, Role, ReaderCardNo)
VALUES
(N'admin', CONVERT(CHAR(64), HASHBYTES('SHA2_256', CONCAT(N'admin', N':', N'admin123', N':', @Salt)), 2), @Salt, N'Admin', NULL),
(N'2024001', CONVERT(CHAR(64), HASHBYTES('SHA2_256', CONCAT(N'2024001', N':', N'reader123', N':', @Salt)), 2), @Salt, N'Reader', N'SYSU-SZ-2024001'),
(N'2024002', CONVERT(CHAR(64), HASHBYTES('SHA2_256', CONCAT(N'2024002', N':', N'reader123', N':', @Salt)), 2), @Salt, N'Reader', N'SYSU-SZ-2024002'),
(N'2024003', CONVERT(CHAR(64), HASHBYTES('SHA2_256', CONCAT(N'2024003', N':', N'reader123', N':', @Salt)), 2), @Salt, N'Reader', N'SYSU-SZ-2024003');

INSERT INTO dbo.BorrowRecords (ReaderCardNo, Isbn, BorrowDate, LoanDays, ReturnDate, Fine, FinePaid)
VALUES
(N'SYSU-SZ-2024001', N'9787111128069', DATEADD(DAY, -12, CAST(GETDATE() AS DATE)), 30, NULL, 0, 1),
(N'SYSU-SZ-2024002', N'9787115428028', DATEADD(DAY, -42, CAST(GETDATE() AS DATE)), 30, NULL, 0, 1),
(N'SYSU-SZ-2024003', N'9787121317985', DATEADD(DAY, -55, CAST(GETDATE() AS DATE)), 30, DATEADD(DAY, -5, CAST(GETDATE() AS DATE)), 10.00, 0);
GO
