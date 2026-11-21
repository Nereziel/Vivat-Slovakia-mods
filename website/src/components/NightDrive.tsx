/** Original vector art: night skyline / car. No external image dependencies. */
export default function NightDrive() {
  return (
    <svg className="night-drive" viewBox="0 0 900 620" aria-hidden="true" focusable="false" preserveAspectRatio="xMidYMid meet">
      <defs>
        <linearGradient id="sky" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor="#171b24" />
          <stop offset="65%" stopColor="#111218" />
          <stop offset="100%" stopColor="#09090d" />
        </linearGradient>
        <linearGradient id="road" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor="#24232b" />
          <stop offset="100%" stopColor="#0a0b0f" />
        </linearGradient>
        <linearGradient id="body" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor="#525660" />
          <stop offset="32%" stopColor="#292c34" />
          <stop offset="82%" stopColor="#11151b" />
          <stop offset="100%" stopColor="#06080b" />
        </linearGradient>
        <linearGradient id="rearGlass" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor="#101a26" />
          <stop offset="100%" stopColor="#07090f" />
        </linearGradient>
        <linearGradient id="redLight" x1="0" x2="1" y1="0" y2="0">
          <stop offset="0" stopColor="#ff3939" />
          <stop offset="60%" stopColor="#f30618" />
          <stop offset="100%" stopColor="#8b0011" />
        </linearGradient>
        <radialGradient id="lightHalo">
          <stop offset="0%" stopColor="#e32030" stopOpacity=".62" />
          <stop offset="45%" stopColor="#a70015" stopOpacity=".17" />
          <stop offset="100%" stopColor="#a70015" stopOpacity="0" />
        </radialGradient>
        <linearGradient id="roadShine" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor="#e33042" stopOpacity=".3" />
          <stop offset="100%" stopColor="#e33042" stopOpacity="0" />
        </linearGradient>
        <pattern id="windows" width="22" height="25" patternUnits="userSpaceOnUse">
          <rect x="7" y="5" width="3" height="7" fill="#778495" opacity=".24" />
          <rect x="16" y="6" width="2" height="7" fill="#ffc07f" opacity=".13" />
        </pattern>
      </defs>
      <rect width="900" height="620" fill="url(#sky)" />
      <circle cx="575" cy="255" r="220" fill="#d8293f" opacity=".08" />
      <path d="M0 300 45 279 73 296 113 276 146 300 181 262 230 288 280 251 330 276 374 240 420 275 476 252 520 286 584 260 623 294 680 252 725 285 781 253 850 288 900 266 V390 H0Z" fill="#161920" />
      <g fill="#11131a" stroke="#33343c" strokeWidth=".7">
        <path d="M0 174H83V365H0Z" /><path d="M43 210H129V366H43Z"/><path d="M113 158H171V364H113Z"/>
        <path d="M190 231H277V365H190Z"/><path d="M290 190H351V365H290Z"/><path d="M354 219H435V365H354Z"/>
        <path d="M466 179H520V369H466Z"/><path d="M530 235H601V367H530Z"/><path d="M616 149H696V367H616Z"/>
        <path d="M703 203H766V367H703Z"/><path d="M774 172H860V367H774Z"/><path d="M860 220H900V367H860Z"/>
      </g>
      <g fill="url(#windows)" opacity=".9">
        <path d="M0 174H83V365H0Z"/><path d="M113 158H171V364H113Z"/><path d="M290 190H351V365H290Z"/>
        <path d="M466 179H520V369H466Z"/><path d="M616 149H696V367H616Z"/><path d="M774 172H860V367H774Z"/>
      </g>
      <path d="M0 364H900V390H0Z" fill="#14151b" />
      <path d="M220 364H680L900 620H0Z" fill="url(#road)" />
      <path d="M223 364 0 620" stroke="#65626a" strokeWidth="1.5" opacity=".22" />
      <path d="M677 364 900 620" stroke="#65626a" strokeWidth="1.5" opacity=".22" />
      <path d="M446 368 450 393" stroke="#d4ab8a" strokeWidth="2" opacity=".3" />
      <path d="M444 409 438 448" stroke="#d4ab8a" strokeWidth="4" opacity=".22" />
      <path d="M437 473 413 610" stroke="#d4ab8a" strokeWidth="9" opacity=".12" />
      <path d="M525 364 900 578" stroke="#e04b50" strokeWidth="2" opacity=".13" />
      <path d="M374 364 10 585" stroke="#e04b50" strokeWidth="2" opacity=".13" />
      <ellipse cx="448" cy="482" rx="390" ry="145" fill="url(#lightHalo)" />
      <path d="M205 491C301 505 600 505 705 492L775 620H118Z" fill="url(#roadShine)" opacity=".54" />
      <ellipse cx="447" cy="553" rx="309" ry="45" fill="#000" opacity=".78" />
      <g transform="translate(0 18)">
        <path d="M237 391 270 308 Q286 273 344 261 L552 261 Q608 272 629 308 L665 392 699 411 714 467 695 500 201 500 183 468 200 410Z" fill="url(#body)" stroke="#767984" strokeWidth="1.6" />
        <path d="M282 374 307 302 Q326 280 363 278H541Q578 282 596 304L623 374Q444 357 282 374Z" fill="url(#rearGlass)" stroke="#555c68" strokeWidth="2.5" />
        <path d="M318 310Q323 293 356 288H541Q570 291 582 310L602 359Q450 347 302 359Z" fill="#111925" opacity=".78" />
        <path d="M311 359 589 359" stroke="#9a9fa8" strokeWidth="2" opacity=".45" />
        <path d="M199 414Q448 430 697 414" stroke="#73747d" strokeWidth="3" opacity=".5" fill="none" />
        <path d="M203 442H694L705 472 688 485H210L192 470Z" fill="#171922" />
        <path d="M204 442H695" stroke="#4a4e5b" strokeWidth="2" />
        <path d="M205 417 320 425 311 451 210 445Z" fill="#6b0c16" stroke="#e21e2e" strokeWidth="2" />
        <path d="M212 425 315 431 310 440 214 436Z" fill="url(#redLight)" />
        <path d="M580 425 692 417 687 445 590 451Z" fill="#6b0c16" stroke="#e21e2e" strokeWidth="2" />
        <path d="M584 431 685 425 682 437 590 440Z" fill="url(#redLight)" />
        <path d="M315 423H580" stroke="#202127" strokeWidth="11" />
        <rect x="407" y="450" width="85" height="24" rx="3" fill="#05060a" stroke="#4d535d" strokeWidth="1" />
        <path d="M430 458H471" stroke="#eee" strokeWidth="3" opacity=".45" />
        <path d="M214 479H680" stroke="#62616c" opacity=".5" />
        <path d="M253 486 270 507H312L323 487ZM577 487 585 507H629L645 485Z" fill="#06070a" />
        <path d="M219 503Q448 522 678 503" stroke="#a11b2a" strokeWidth="2" opacity=".3" fill="none" />
      </g>
      <path d="M0 580H900V620H0Z" fill="#090a0d" opacity=".45" />
    </svg>
  );
}
