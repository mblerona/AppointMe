// Shared by the appointment Create and Edit forms: the expected duration, and what may block saving.
// The server makes the final decision on every rule. This only warns earlier, and it blocks Save
// only when the server would certainly refuse, so it must never be stricter than the server.
window.AppointmentForm = (function () {
    "use strict";

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

    // The one place that sets the date field's validity and the Save button. Each check reports its
    // own reason, so one check clearing its problem can't undo another check's block.
    function createSaveGuard(input, submitBtn) {
        const reasons = new Map();

        function apply() {
            const first = reasons.values().next().value;
            input.setCustomValidity(first || "");
            if (submitBtn) submitBtn.disabled = reasons.size > 0;
        }

        return {
            block(reason, message) { reasons.set(reason, message); apply(); },
            clear(reason) { reasons.delete(reason); apply(); }
        };
    }

    function initDurationHint(input, guard) {
        const hint = document.getElementById("durationHint");
        const warning = document.getElementById("closingWarning");
        if (!hint || !warning) return;

        const data = hint.dataset;
        const slotMinutes = Number(data.slotMinutes);
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
                const start = new Date(input.value);
                const startMinutes = start.getHours() * 60 + start.getMinutes();
                const end = startMinutes + duration;
                overClosing = end > closingMinutes;
                hint.textContent += ", ends at " + pad(Math.floor(end / 60) % 24) + ":" + pad(end % 60);
            }

            // On Edit the server only runs this check for a moved date, or for services that make a
            // non-cancelled appointment longer, so that older bookings stay editable. Same here.
            const serverWouldCheck = !isEdit
                || input.value !== storedDate
                || (duration > storedDuration && (!statusSelect || statusSelect.value !== "Cancelled"));

            if (overClosing && serverWouldCheck) {
                const message = "This appointment takes " + duration + " minutes and would end after closing time (" + data.workEnd + "). Please choose an earlier time.";
                warning.textContent = message;
                warning.classList.remove("d-none");
                guard.block("closing", message);
            } else {
                warning.textContent = "";
                warning.classList.add("d-none");
                guard.clear("closing");
            }
        }

        input.addEventListener("change", update);
        input.addEventListener("input", update);
        checkboxes.forEach(c => c.addEventListener("change", update));
        if (statusSelect) statusSelect.addEventListener("change", update);

        update();
    }

    return { calculateDuration, formatDuration, createSaveGuard, initDurationHint };
})();
