import Foundation

enum PlannerDates {
    static var calendar: Calendar {
        var value = Calendar(identifier: .gregorian)
        value.timeZone = TimeZone(secondsFromGMT: 0)!
        value.firstWeekday = 2
        return value
    }
    static func formatter(_ timezone: TimeZone) -> DateFormatter {
        let value = DateFormatter(); value.locale = Locale(identifier: "en_US_POSIX")
        value.calendar = calendar; value.timeZone = timezone; value.dateFormat = "yyyy-MM-dd"
        return value
    }
    static func date(_ text: String) -> Date { formatter(calendar.timeZone).date(from: text) ?? Date(timeIntervalSince1970: 0) }
    static func string(_ date: Date) -> String { formatter(calendar.timeZone).string(from: date) }
    static func today(timezone: String, now: Date = Date()) -> String { formatter(TimeZone(identifier: timezone) ?? calendar.timeZone).string(from: now) }
    static func add(_ text: String, days: Int) -> String { string(calendar.date(byAdding: .day, value: days, to: date(text))!) }
    static func monday(_ text: String) -> String { add(text, days: -((calendar.component(.weekday, from: date(text)) + 5) % 7)) }
}
