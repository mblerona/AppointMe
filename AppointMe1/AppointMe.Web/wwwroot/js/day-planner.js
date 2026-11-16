// Day planner on the Create Appointment form: shows one day's slots from /Appointments/DayAvailability
// and fills the date field when a free slot is clicked. It only informs and never blocks Save; the
// server checks every rule again (and appointment-form.js blocks what the server would certainly refuse).
window.DayPlanner = (function () {
    "use strict";

    const DAY_NAMES = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    function pad(n) { return (n < 10 ? "0" : "") + n; }
    function hhmm(minutes) { return pad(Math.floor(minutes / 60) % 24) + ":" + pad(minutes % 60); }
    function ymd(d) { return d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate()); }

    // Dates are handled as plain calendar days (local midnight) so the browser's time zone never matters.
    function parseYmd(s) {
        const [y, m, d] = s.split("-").map(Number);
        return new Date(y, m - 1, d);
    }

    function addDays(s, days) {
        const d = parseYmd(s);
        d.setDate(d.getDate() + days);
        return ymd(d);
    }

    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    }

    function icon(name) {
        const i = el("i", "bi bi-" + name);
        i.setAttribute("aria-hidden", "true");
        return i;
    }

    function init(options) {
        const input = options.input;
        const root = options.container;
        if (!input || !root) return;

        const checkboxes = Array.from(input.form.querySelectorAll('input[name="ServiceOfferingIds"]'));
        const cache = new Map();
        const today = ymd(AppointmentForm.now());
        let day = input.value ? input.value.slice(0, 10) : today;
        let data = null;
        let loading = 0;

        // Same calculation as the form's duration hint (AppointmentForm.calculateDuration).
        function duration(slotMinutes) {
            const selected = checkboxes.filter(c => c.checked)
                .map(c => c.dataset.durationMinutes ? Number(c.dataset.durationMinutes) : null);
            return AppointmentForm.calculateDuration(slotMinutes, selected);
        }

        // The start time picked in the form, in minutes, when it is on the day being shown.
        function pickedMinute() {
            if (!input.value || input.value.slice(0, 10) !== day) return null;
            const [h, m] = input.value.slice(11, 16).split(":").map(Number);
            return h * 60 + m;
        }

        // Mirrors the server: the booking must end by closing and must not overlap any non-cancelled booking.
        function fits(start, minutes) {
            const end = start + minutes;
            if (end > data.closingMinute) return false;
            return !data.bookings.some(b => b.startMinute < end && b.endMinute > start);
        }

        async function load(target) {
            day = target;
            const ticket = ++loading;

            if (!cache.has(day)) {
                renderSkeleton();
                try {
                    const res = await fetch(options.url + "?date=" + encodeURIComponent(day), {
                        headers: { "Accept": "application/json" }
                    });
                    if (!res.ok) throw new Error(String(res.status));
                    cache.set(day, await res.json());
                } catch (e) {
                    if (ticket === loading) renderError();
                    return;
                }
            }

            if (ticket !== loading) return; // a newer day was asked for meanwhile
            data = cache.get(day);
            render();
        }

        function pick(minute) {
            input.value = day + "T" + hhmm(minute);
            // The form's own listeners (holiday check, duration hint, interval) react to these.
            input.dispatchEvent(new Event("input", { bubbles: true }));
            input.dispatchEvent(new Event("change", { bubbles: true }));
            render();
        }

        function header() {
            const head = el("div", "planner-head");

            const prev = el("button", "btn btn-light planner-nav");
            prev.type = "button";
            prev.setAttribute("aria-label", "Previous day");
            prev.appendChild(icon("chevron-left"));
            prev.disabled = day <= today;
            prev.addEventListener("click", () => load(addDays(day, -1)));

            const next = el("button", "btn btn-light planner-nav");
            next.type = "button";
            next.setAttribute("aria-label", "Next day");
            next.appendChild(icon("chevron-right"));
            next.addEventListener("click", () => load(addDays(day, 1)));

            const d = parseYmd(day);
            const label = el("div", "planner-day");
            label.appendChild(el("div", "planner-day-name", DAY_NAMES[d.getDay()]));
            const sub = d.getDate() + " " + MONTHS[d.getMonth()] + " " + d.getFullYear();
            label.appendChild(el("div", "planner-day-sub",
                day === today ? "Today · " + sub : day === addDays(today, 1) ? "Tomorrow · " + sub : sub));

            head.append(prev, label, next);
            return head;
        }

        function renderShell() {
            root.replaceChildren(header());
        }

        function renderSkeleton() {
            renderShell();
            const grid = el("div", "planner-slots");
            for (let i = 0; i < 12; i++) grid.appendChild(el("div", "planner-skeleton"));
            root.appendChild(grid);
        }

        function renderError() {
            renderShell();
            const box = el("div", "planner-empty");
            box.appendChild(icon("wifi-off"));
            box.appendChild(el("div", "fw-semibold", "Couldn't load this day"));
            const retry = el("button", "btn btn-sm btn-outline-secondary mt-2", "Try again");
            retry.type = "button";
            retry.addEventListener("click", () => load(day));
            box.appendChild(retry);
            root.appendChild(box);
        }

        function renderClosed() {
            const box = el("div", "planner-empty");
            box.appendChild(icon(data.holidayName ? "balloon" : "moon-stars"));
            box.appendChild(el("div", "fw-semibold text-dark",
                data.holidayName ? "Public holiday" : "Closed on " + DAY_NAMES[parseYmd(day).getDay()] + "s"));
            if (data.holidayName) box.appendChild(el("div", "small", data.holidayName));
            const next = el("button", "btn btn-sm btn-outline-primary mt-3");
            next.type = "button";
            next.append("Next day ", icon("arrow-right"));
            next.addEventListener("click", () => load(addDays(day, 1)));
            box.appendChild(next);
            root.appendChild(box);
        }

        function render() {
            renderShell();
            if (!data.isOpen) {
                renderClosed();
                renderBookings();
                return;
            }

            const minutes = duration(data.slotMinutes);
            const picked = pickedMinute();
            const free = data.slots.filter(s => s.state === "free");
            const fitting = free.filter(s => fits(s.startMinute, minutes));

            // Stats
            const stats = el("div", "planner-stats");
            [
                [fitting.length, "Free for " + AppointmentForm.formatDuration(minutes), "is-free"],
                [data.slots.filter(s => s.state === "booked").length, "Booked slots", ""],
                [data.bookings.length, data.bookings.length === 1 ? "Appointment" : "Appointments", ""]
            ].forEach(([value, label, cls]) => {
                const s = el("div", "planner-stat " + cls);
                s.appendChild(el("div", "planner-stat-value", String(value)));
                s.appendChild(el("div", "planner-stat-label", label));
                stats.appendChild(s);
            });
            root.appendChild(stats);

            // Strip: the working day to scale.
            const span = data.closingMinute - data.openingMinute;
            const pct = m => Math.max(0, Math.min(100, (m - data.openingMinute) / span * 100));
            const strip = el("div", "planner-strip");
            strip.style.setProperty("--slot-pct", (data.slotMinutes / span * 100) + "%");
            strip.setAttribute("aria-hidden", "true");

            const pastSlots = data.slots.filter(s => s.state === "past");
            if (pastSlots.length) {
                const past = el("div", "planner-strip-past");
                past.style.width = pct(pastSlots[pastSlots.length - 1].startMinute + data.slotMinutes) + "%";
                strip.appendChild(past);
            }
            data.bookings.forEach(b => {
                const block = el("div", "planner-strip-booking");
                block.style.left = pct(b.startMinute) + "%";
                block.style.width = (pct(b.endMinute) - pct(b.startMinute)) + "%";
                strip.appendChild(block);
            });
            if (picked !== null) {
                const mark = el("div", "planner-strip-pick" + (fits(picked, minutes) ? "" : " is-short"));
                mark.style.left = pct(picked) + "%";
                mark.style.width = (pct(picked + minutes) - pct(picked)) + "%";
                strip.appendChild(mark);
            }
            root.appendChild(strip);

            const labels = el("div", "planner-strip-labels");
            labels.append(el("span", "", hhmm(data.openingMinute)), el("span", "", hhmm(data.closingMinute)));
            root.appendChild(labels);

            // Slots
            const grid = el("div", "planner-slots");
            grid.setAttribute("role", "group");
            grid.setAttribute("aria-label", "Start times");

            data.slots.forEach(s => {
                const btn = el("button", "planner-slot");
                btn.type = "button";
                btn.appendChild(document.createTextNode(hhmm(s.startMinute)));

                const isPicked = picked === s.startMinute;
                const covered = picked !== null && s.startMinute > picked && s.startMinute < picked + minutes;

                let note;
                if (s.state === "booked") {
                    btn.classList.add("is-booked");
                    btn.disabled = true;
                    note = "Booked";
                } else if (s.state === "past") {
                    btn.classList.add("is-past");
                    btn.disabled = true;
                    note = "Past";
                } else if (fits(s.startMinute, minutes)) {
                    btn.classList.add("is-free");
                    note = "Free";
                    btn.title = "Start at " + hhmm(s.startMinute) + ", ends " + hhmm(s.startMinute + minutes);
                } else {
                    // Free, but the selected services don't fit before the next booking or closing time.
                    btn.classList.add("is-short");
                    btn.disabled = true;
                    note = "Too short";
                    btn.title = "Needs " + AppointmentForm.formatDuration(minutes) + "; there isn't enough time before the next booking or closing.";
                }

                if (isPicked) btn.classList.add("is-picked");
                else if (covered && s.state !== "booked") btn.classList.add("is-covered");
                btn.setAttribute("aria-pressed", isPicked ? "true" : "false");

                btn.appendChild(el("small", "", isPicked ? "Selected" : note));
                if (!btn.disabled) btn.addEventListener("click", () => pick(s.startMinute));
                grid.appendChild(btn);
            });
            root.appendChild(grid);

            if (data.slots.length > 0 && fitting.length === 0) {
                const none = el("div", "alert alert-warning small mt-3 mb-0 d-flex gap-2 align-items-start");
                none.append(icon("hourglass-split"), document.createTextNode(
                    "No free time for " + AppointmentForm.formatDuration(minutes) + " on this day. Try the next day."));
                root.appendChild(none);
            }

            renderBookings();

            const legend = el("div", "planner-legend");
            [["lg-free", "Free"], ["lg-short", "Not enough time"], ["lg-booked", "Booked"], ["lg-picked", "Your pick"]]
                .forEach(([cls, text]) => legend.appendChild(el("span", cls, text)));
            root.appendChild(legend);
        }

        function renderBookings() {
            if (!data.bookings.length) return;
            root.appendChild(el("div", "planner-section-title", "On this day"));
            data.bookings.forEach(b => {
                const row = el("a", "planner-booking");
                row.href = b.url;
                row.target = "_blank";
                row.rel = "noopener";
                row.title = "Open " + b.orderNumber + " in a new tab";
                row.appendChild(el("span", "planner-booking-time", hhmm(b.startMinute) + "–" + hhmm(b.endMinute)));
                const who = el("span", "planner-booking-who");
                who.appendChild(el("span", "fw-semibold", b.customerName || "—"));
                who.appendChild(el("span", "text-muted small", " · " + b.orderNumber));
                row.appendChild(who);
                row.appendChild(icon("box-arrow-up-right"));
                root.appendChild(row);
            });
        }

        // The form may move the date (closed day, past time, interval), so follow the field.
        input.addEventListener("change", function () {
            if (!input.value) return;
            const fieldDay = input.value.slice(0, 10);
            if (fieldDay !== day) load(fieldDay);
            else if (data) render();
        });
        checkboxes.forEach(c => c.addEventListener("change", () => { if (data) render(); }));

        load(day < today ? today : day);
    }

    return { init };
})();
