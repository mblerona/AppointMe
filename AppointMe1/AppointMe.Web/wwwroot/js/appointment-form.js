// Shared by the appointment Create and Edit forms: the expected duration, and what may block saving.
// The server makes the final decision on every rule. This only warns earlier, and it blocks Save
// only when the server would certainly refuse, so it must never be stricter than the server.
window.AppointmentForm = (function () {
    "use strict";

    // jquery.validate throws on a step attribute on datetime-local, which stops all client validation
    // and lets an invalid form be submitted. The browser and the server both check this field anyway.
    // (data-rule-step="false" doesn't help: this version skips a false value and keeps the rule.)
    if (window.jQuery && jQuery.validator) {
        const originalStep = jQuery.validator.methods.step;
        jQuery.validator.methods.step = function (value, element, param) {
            return element.type === "datetime-local" ? true : originalStep.call(this, value, element, param);
        };
    }

    // Same rule as AppointmentDurationCalculator on the server: the sum of the services, where a
    // service without a duration counts as one slot, rounded up to a multiple of the slot length.
    function calculateDuration(slotMinutes, serviceDurations) {
        const total = serviceDurations.reduce((sum, d) => sum + (d === null ? slotMinutes : d), 0);
        return total <= 0 ? slotMinutes : Math.ceil(total / slotMinutes) * slotMinutes;
    }

    // Same text as AppointmentDTO.DurationDisplay.
    function formatDuration(minutes) {
        if (minutes < 60) return minutes + " min";
        const hours = Math.floor(minutes / 60);
        const rest = minutes % 60;
        return rest === 0 ? hours + " h" : hours + " h " + rest + " min";
    }

    function toMinutes(hhmm) {
        const [h, m] = hhmm.split(":").map(Number);
        return h * 60 + m;
    }

    function pad(n) { return (n < 10 ? "0" : "") + n; }

    function formatTime(minutes) {
        return pad(Math.floor(minutes / 60) % 24) + ":" + pad(minutes % 60);
    }

    function toFieldValue(d) {
        return d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate()) +
            "T" + pad(d.getHours()) + ":" + pad(d.getMinutes());
    }

    function minuteOfDay(d) {
        return d.getHours() * 60 + d.getMinutes();
    }

    // The next start time on the interval, counted from opening time, at or after this value.
    function nextOnInterval(value, slotMinutes, openingMinutes) {
        const d = new Date(value);
        const since = minuteOfDay(d) - openingMinutes;
        const steps = since <= 0 ? 0 : Math.ceil(since / slotMinutes);
        d.setHours(0, openingMinutes + steps * slotMinutes, 0, 0);
        return toFieldValue(d);
    }

    // The one place that sets the date field's validity and the Save button. Each check reports its
    // own reason, so one check clearing its problem can't undo another check's block. Save is disabled
    // while any check blocks, and once the form has really been submitted.
    function createSaveGuard(input, submitBtn) {
        const reasons = new Map();
        let submitted = false;

        function apply() {
            const first = reasons.values().next().value;
            input.setCustomValidity(first || "");
            if (submitBtn) submitBtn.disabled = reasons.size > 0 || submitted;
        }

        // On the document, so it runs after the form's own handlers. jQuery validation stops an invalid
        // submit before it gets here, and that form must stay usable so the errors can be fixed.
        document.addEventListener("submit", function (e) {
            if (e.target === input.form && !e.defaultPrevented) {
                submitted = true;
                apply();
            }
        });

        // Going Back can restore this page exactly as it was left, with Save disabled.
        window.addEventListener("pageshow", function (e) {
            if (e.persisted) {
                submitted = false;
                apply();
            }
        });

        apply();

        return {
            block(reason, message) { reasons.set(reason, message); apply(); },
            clear(reason) { reasons.delete(reason); apply(); }
        };
    }

    // For a new booking only: the date picker offers the interval's times. The browser counts step from
    // min, not from opening time, so min must be on the interval too. A day has to divide evenly into
    // intervals, or a later day's times wouldn't line up and the browser would refuse valid ones.
    // Edit doesn't use this: a stored date off the interval would be refused by the browser.
    function prepareNewBookingDate(input) {
        const data = document.getElementById("durationHint").dataset;
        const slotMinutes = Number(data.slotMinutes);
        const openingMinutes = toMinutes(data.workStart);

        const firstFree = nextOnInterval(new Date(), slotMinutes, openingMinutes);
        input.min = firstFree;
        input.step = 1440 % slotMinutes === 0 ? slotMinutes * 60 : 60;

        if (input.value) input.value = nextOnInterval(input.value, slotMinutes, openingMinutes);
    }

    function initDurationHint(input, guard) {
        const hint = document.getElementById("durationHint");
        const warning = document.getElementById("timeWarning");
        if (!hint || !warning) return;

        const data = hint.dataset;
        const slotMinutes = Number(data.slotMinutes);
        const openingMinutes = toMinutes(data.workStart);
        const closingMinutes = toMinutes(data.workEnd);
        const servicesEnabled = data.servicesEnabled === "true";

        // Only the Edit form has these: what the appointment has stored right now.
        const isEdit = data.storedDuration !== undefined;
        const storedDuration = isEdit ? Number(data.storedDuration) : null;
        const storedDate = data.storedDate;
        const storedServices = (data.storedServices || "").split(",").filter(Boolean);

        const checkboxes = Array.from(input.form.querySelectorAll('input[name="ServiceOfferingIds"]'));
        const statusSelect = input.form.querySelector('select[name="Status"]');

        function servicesChanged(selected) {
            if (!isEdit || !servicesEnabled) return false;
            const ids = selected.map(c => c.value);
            return ids.length !== storedServices.length || ids.some(id => !storedServices.includes(id));
        }

        // The server checks a new or moved date. An unchanged one stays allowed, whatever it is.
        function dateIsNewOrMoved() {
            return !isEdit || input.value !== storedDate;
        }

        function isOffInterval() {
            if (!input.value) return false;
            return (minuteOfDay(new Date(input.value)) - openingMinutes) % slotMinutes !== 0;
        }

        function intervalMessage() {
            const since = minuteOfDay(new Date(input.value)) - openingMinutes;
            const earlier = openingMinutes + Math.floor(since / slotMinutes) * slotMinutes;
            const later = earlier + slotMinutes;
            const suggestion = later < closingMinutes
                ? "Try " + formatTime(earlier) + " or " + formatTime(later) + "."
                : "Try " + formatTime(earlier) + ".";
            return "Appointments start every " + slotMinutes + " minutes from opening time (" + data.workStart + "). " + suggestion;
        }

        function showWarning(message) {
            warning.textContent = message;
            warning.classList.toggle("d-none", !message);
        }

        function update() {
            const selected = servicesEnabled ? checkboxes.filter(c => c.checked) : [];

            // While the selection is unchanged, the stored duration is the snapshot that will stay.
            const changed = servicesChanged(selected);
            const duration = isEdit && !changed
                ? storedDuration
                // Razor renders a service without a duration as an empty attribute, which means one slot.
                : calculateDuration(slotMinutes, selected.map(c => c.dataset.durationMinutes ? Number(c.dataset.durationMinutes) : null));

            hint.textContent = "Expected duration: " + formatDuration(duration);

            let overClosing = false;
            if (input.value) {
                const end = minuteOfDay(new Date(input.value)) + duration;
                overClosing = end > closingMinutes;
                hint.textContent += ", ends at " + formatTime(end);
            }

            // Off the interval: only a new or moved date is checked.
            const offInterval = dateIsNewOrMoved() && isOffInterval();

            // Past closing: on Edit the server also checks services that make a non-cancelled
            // appointment longer. Otherwise older bookings stay editable. Same here.
            const longerAndActive = duration > storedDuration && (!statusSelect || statusSelect.value !== "Cancelled");
            const pastClosing = overClosing && (dateIsNewOrMoved() || longerAndActive);
            const closingMessage = "This appointment takes " + duration + " minutes and would end after closing time (" + data.workEnd + "). Please choose an earlier time.";

            if (offInterval) guard.block("interval", intervalMessage()); else guard.clear("interval");
            if (pastClosing) guard.block("closing", closingMessage); else guard.clear("closing");

            // One message at a time, in the same order the server checks them.
            showWarning(offInterval ? intervalMessage() : pastClosing ? closingMessage : "");
        }

        input.addEventListener("change", update);
        input.addEventListener("input", update);
        checkboxes.forEach(c => c.addEventListener("change", update));
        if (statusSelect) statusSelect.addEventListener("change", update);

        // Not while typing: rewriting the field after each digit would make "10:45" impossible to type.
        input.addEventListener("blur", function () {
            if (input.value && dateIsNewOrMoved() && isOffInterval()) {
                input.value = nextOnInterval(input.value, slotMinutes, openingMinutes);
                update();
            }
        });

        update();
    }

    return { calculateDuration, formatDuration, nextOnInterval, createSaveGuard, prepareNewBookingDate, initDurationHint };
})();
