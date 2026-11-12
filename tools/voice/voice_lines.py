"""Create a robot 'Reactor Assistant' Voicebox profile and render the reactor-sim
assistant/autopilot lines to WAV via the local Voicebox API (127.0.0.1:17493). Writes ~/Downloads/reactor-voice/.
Nothing is played back."""
import json
import pathlib
import time
import urllib.error
import urllib.request

API = "http://127.0.0.1:17493"
OUT = pathlib.Path.home() / "Downloads" / "reactor-voice"

LINES = {
    "startup_begin": "Beginning cold startup. Reactor is shut down and subcritical.",
    "step_pumps": "Starting reactor coolant pumps. Flow coming up.",
    "step_pressurize": "Pressurizer heaters on. Raising primary pressure to sixty bar.",
    "step_vacuum": "Cooling water on. Drawing condenser vacuum.",
    "step_shutdown_banks": "Withdrawing shutdown banks D, C and B.",
    "step_dilute": "Diluting boron toward twelve hundred P P M.",
    "step_bank_a": "Withdrawing bank A. Moving slowly. Watching flux.",
    "critical": "Reactor is critical. Holding power steady.",
    "heatup": "Heating the primary. Steam draw held below reactor power.",
    "heatup_done": "Primary at two hundred fifty degrees. Heat-up complete.",
    "turbine_roll": "Rolling the turbine to rated speed.",
    "sync": "Generator synchronized. Breaker closed.",
    "power_ascension": "Raising power. Steam matched to reactor output.",
    "autopilot_on": "Autopilot engaged. I have the controls.",
    "autopilot_off": "Autopilot disengaged. You have the controls.",
    "advise_slow": "Recommend slower rod motion. Reactor period is short.",
    "advise_borate": "Recommend boration. Boron reserve is low.",
    "advise_xenon": "Xenon is building. Expect power to drift down.",
    "alarm_power_high": "Warning. Reactor power high.",
    "alarm_pressure_high": "Warning. Primary pressure high.",
    "alarm_temp_high": "Warning. Fuel temperature high.",
    "alarm_period": "Warning. Short reactor period.",
    "scram": "Reactor scram. All rods inserted.",
    "scram_reset": "Scram reset. Rod withdrawal permitted.",
    "turbine_trip": "Turbine trip.",
    "all_normal": "All parameters normal.",
}

ROBOT_CHAIN = [
    {"type": "pitch_shift", "enabled": True, "params": {"semitones": -2.0}},
    {"type": "chorus", "enabled": True, "params": {"rate_hz": 0.2, "depth": 1.0, "feedback": 0.35, "centre_delay_ms": 7.0, "mix": 0.5}},
    {"type": "highpass", "enabled": True, "params": {"cutoff_frequency_hz": 180.0}},
    {"type": "lowpass", "enabled": True, "params": {"cutoff_frequency_hz": 5000.0}},
    {"type": "compressor", "enabled": True, "params": {"threshold_db": -18.0, "ratio": 4.0}},
]


def call(method: str, path: str, body: dict | None = None, raw: bool = False):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(API + path, data=data, method=method, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=600) as r:
        payload = r.read()
    return payload if raw else (json.loads(payload) if payload else None)


def get_profile() -> str:
    for p in call("GET", "/profiles"):
        if p["name"] == "Reactor Assistant":
            return p["id"]
    p = call("POST", "/profiles", {
        "name": "Reactor Assistant", "description": "Reactor Sim control-room assistant (robot)",
        "language": "en", "voice_type": "preset", "preset_engine": "kokoro", "preset_voice_id": "am_onyx",
        "default_engine": "kokoro",
    })
    call("PUT", f"/profiles/{p['id']}/effects", {"effects_chain": ROBOT_CHAIN})
    return p["id"]


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    pid = get_profile()
    print("profile", pid)
    for name, text in LINES.items():
        gen = call("POST", "/generate", {"profile_id": pid, "text": text, "language": "en", "engine": "kokoro", "seed": 7})
        gid = gen["id"]
        st: dict = {}
        for _ in range(600):
            try:
                st = call("GET", f"/history/{gid}") or {}
            except urllib.error.HTTPError:
                st = {}
            if st.get("status") in ("completed", "complete", "done", "failed", "error"):
                break
            time.sleep(1)
        if not st:
            print("NO STATUS", name)
            continue
        if st.get("status") in ("failed", "error"):
            print("FAILED", name, st)
            continue
        audio = call("GET", f"/audio/{gid}", raw=True)
        (OUT / f"{name}.wav").write_bytes(audio)
        print("ok", name, len(audio))


if __name__ == "__main__":
    main()
