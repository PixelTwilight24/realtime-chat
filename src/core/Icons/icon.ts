import { FaIconLibrary } from '@fortawesome/angular-fontawesome';
import {
  faUser,
  faHome,
  faCoffee,
  faAngleLeft,
  faEllipsis,
  faMarsStroke,
  faMagnifyingGlass,
  faPaperPlane,
  faXmark,
  faBellSlash,
  faBan,
  faComments,
  faRightFromBracket,
  faPaperclip,
  faFile,
  faDownload,
} from '@fortawesome/free-solid-svg-icons';

export function registerIcons(library: FaIconLibrary) {
  library.addIcons(
    faUser,
    faHome,
    faCoffee,
    faAngleLeft,
    faEllipsis,
    faMarsStroke,
    faMagnifyingGlass,
    faPaperPlane,
    faXmark,
    faBellSlash,
    faBan,
    faComments,
    faRightFromBracket,
    faPaperclip,
    faFile,
    faDownload
  );
}